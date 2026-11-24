using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Clustering;
using NextMqtt.Broker.Diagnostics;
using NextMqtt.Broker.Messaging;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.RateLimiting;
using NextMqtt.Broker.Security;
using NextMqtt.Broker.Sessions;
using NextMqtt.Broker.Storage;
using NextMqtt.Broker.Subscriptions;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker;

/// <summary>Result of binding a connection to a session.</summary>
public sealed record SessionAttachResult(
    BrokerSession Session,
    bool SessionPresent,
    IClientConnection? PreviousConnection);

/// <summary>
/// The broker facade: authentication, session lifecycle, routing, retained
/// messages and administration.
/// </summary>
public sealed class MqttBroker : IAsyncDisposable
{
    private readonly ILogger<MqttBroker> _logger;
    private readonly SemaphoreSlim _attachGate = new(1, 1);
    private readonly Dictionary<string, CancellationTokenSource> _pendingWills = new(StringComparer.Ordinal);
    private readonly object _willGate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _maintenanceTask;
    private bool _disposed;

    public MqttBroker(
        BrokerOptions options,
        IClientAuthenticator authenticator,
        ITopicAuthorizer authorizer,
        IClusterRouter cluster,
        ILogger<MqttBroker> logger,
        IRetainedJournal? retainedJournal = null)
    {
        Options = options;
        Authenticator = authenticator;
        Authorizer = authorizer;
        Cluster = cluster;
        ConnectionLimits = new ConnectionRateLimiter(options.RateLimits);
        Retained = new RetainedMessageStore(retainedJournal);
        _logger = logger;
    }

    public BrokerOptions Options { get; }

    public IClientAuthenticator Authenticator { get; }

    public ITopicAuthorizer Authorizer { get; }

    public IClusterRouter Cluster { get; }

    /// <summary>Per-address CONNECT limiter, disabled unless configured.</summary>
    public ConnectionRateLimiter ConnectionLimits { get; }

    public SessionRegistry Sessions { get; } = new();

    public SubscriptionIndex Subscriptions { get; } = new();

    public RetainedMessageStore Retained { get; }

    public BrokerMetrics Metrics { get; } = new();

    public RateTracker PublishRate { get; } = new();

    public RateTracker DeliveryRate { get; } = new();

    public ValueTask<AuthenticationResult> AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken = default)
        => Authenticator.AuthenticateAsync(context, cancellationToken);

    public async Task<SessionAttachResult> AttachSessionAsync(
        ConnectPacket connect,
        ClientPrincipal principal,
        IClientConnection connection,
        CancellationToken cancellationToken = default)
    {
        await _attachGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        BrokerSession session;
        IClientConnection? previous;
        bool sessionPresent;
        bool removeDiscarded = false;

        try
        {
            var existing = Sessions.Get(connect.ClientId);
            var persistent = ResolvePersistence(connect, out var expiry);

            if (existing is not null
                && (connect.CleanStart || existing.ProtocolVersion != connect.ProtocolVersion))
            {
                previous = existing.Connection;
                Sessions.Remove(existing.ClientId);
                Subscriptions.RemoveAll(existing.ClientId);
                removeDiscarded = true;
                existing.Dispose();
                existing = null;
            }
            else
            {
                previous = existing?.Connection;
            }

            if (existing is null)
            {
                session = new BrokerSession(connect.ClientId, Options, Metrics);
                if (!Sessions.TryAdd(session))
                {
                    session.Dispose();
                    throw new InvalidOperationException($"Session '{connect.ClientId}' already exists.");
                }

                sessionPresent = false;
            }
            else
            {
                session = existing;
                sessionPresent = true;
            }

            session.Bind(connection, connect, principal, persistent, expiry);
            CancelPendingWill(connect.ClientId);
        }
        finally
        {
            _attachGate.Release();
        }

        if (removeDiscarded)
        {
            _logger.LogDebug("Discarded previous session state for client {ClientId}", connect.ClientId);
        }

        if (previous is not null && !ReferenceEquals(previous, connection))
        {
            await previous.DisconnectAsync(
                MqttReasonCodes.SessionTakenOver,
                "Session taken over by a new connection.",
                cancellationToken).ConfigureAwait(false);
        }

        if (sessionPresent)
        {
            session.Resume();
        }

        return new SessionAttachResult(session, sessionPresent, previous);
    }

    public async Task OnConnectionClosedAsync(
        BrokerSession session,
        IClientConnection connection,
        DisconnectPacket? disconnect,
        bool graceful,
        CancellationToken cancellationToken = default)
    {
        if (!session.Detach(connection))
        {
            return;
        }

        Metrics.ConnectionClosed();

        if (disconnect is not null
            && session.ProtocolVersion == MqttProtocolVersion.V500
            && disconnect.Properties.GetUInt32(MqttPropertyId.SessionExpiryInterval) is { } requestedExpiry)
        {
            session.UpdateSessionExpiry(requestedExpiry);
        }

        var will = session.Will;
        var publishWill = !graceful
            || (disconnect is not null && disconnect.ReasonCode == MqttReasonCodes.DisconnectWithWill);

        var remove = !session.Persistent || session.SessionExpiryInterval == 0;

        if (remove)
        {
            RemoveSession(session);
        }
        else
        {
            session.ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(session.SessionExpiryInterval);
        }

        if (publishWill && will is not null)
        {
            ScheduleWill(session.ClientId, will, session.SessionExpiryInterval);
        }
        else
        {
            session.ClearWill();
        }

        await Task.CompletedTask;
    }

    /// <summary>Routes a publish to local subscribers and, optionally, to peers.</summary>
    public async ValueTask RouteAsync(
        PublishPacket publish,
        string? sourceClientId,
        CancellationToken cancellationToken = default,
        bool forwardToCluster = true)
    {
        if (!MqttTopic.IsValidTopicName(publish.Topic, out _))
        {
            _logger.LogWarning("Dropping publish with invalid topic");
            Metrics.MessageDropped();
            return;
        }

        var messageExpiry = publish.Properties.GetUInt32(MqttPropertyId.MessageExpiryInterval);

        if (messageExpiry == 0)
        {
            if (publish.Retain && publish.Payload.Length == 0)
            {
                Retained.Remove(publish.Topic);
            }

            Metrics.MessageDropped();
            return;
        }

        if (publish.Retain)
        {
            Retained.Publish(publish, DateTimeOffset.UtcNow);
        }

        Metrics.PublishReceived();
        PublishRate.Increment();

        var matches = Subscriptions.Match(publish.Topic);
        if (matches.Count > 0)
        {
            var deliveries = new Dictionary<string, DeliveryAggregate>(StringComparer.Ordinal);

            foreach (var match in matches)
            {
                var subscription = match.Subscription;

                if (subscription.Options.NoLocal
                    && sourceClientId is not null
                    && string.Equals(subscription.ClientId, sourceClientId, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!deliveries.TryGetValue(subscription.ClientId, out var aggregate))
                {
                    aggregate = new DeliveryAggregate();
                    deliveries[subscription.ClientId] = aggregate;
                }

                var qos = (MqttQoS)Math.Min((int)publish.QoS, (int)subscription.Options.QoS);
                if (qos > aggregate.QoS)
                {
                    aggregate.QoS = qos;
                }

                aggregate.RetainAsPublished |= subscription.Options.RetainAsPublished;

                if (subscription.Identifier is { } identifier)
                {
                    aggregate.SubscriptionIdentifiers.Add(identifier);
                }
            }

            foreach (var (clientId, aggregate) in deliveries)
            {
                var session = Sessions.Get(clientId);
                if (session is null)
                {
                    continue;
                }

                var message = BuildOutboundMessage(publish, aggregate);
                if (session.Deliver(message))
                {
                    DeliveryRate.Increment();
                }
            }
        }

        if (forwardToCluster && Cluster.Enabled)
        {
            await Cluster.RouteAsync(publish, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Entry point used by the cluster transport for remote publishes.</summary>
    public ValueTask RouteFromClusterAsync(PublishPacket publish, CancellationToken cancellationToken = default)
        => RouteAsync(publish, sourceClientId: null, cancellationToken, forwardToCluster: false);

    public async ValueTask PublishAsync(
        string topic,
        byte[] payload,
        MqttQoS qos = MqttQoS.AtMostOnce,
        bool retain = false,
        CancellationToken cancellationToken = default)
    {
        if (!MqttTopic.IsValidTopicName(topic, out var error))
        {
            throw new ArgumentException(error, nameof(topic));
        }

        var publish = new PublishPacket
        {
            Topic = topic,
            Payload = payload,
            QoS = qos,
            Retain = retain,
        };

        await RouteAsync(publish, sourceClientId: null, cancellationToken).ConfigureAwait(false);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _maintenanceTask ??= Task.Run(() => MaintenanceLoopAsync(_lifetime.Token), CancellationToken.None);
        return Cluster.StartAsync(cancellationToken).AsTask();
    }

    public async Task StopAsync()
    {
        _lifetime.Cancel();
        await Cluster.StopAsync().ConfigureAwait(false);

        if (_maintenanceTask is not null)
        {
            try
            {
                await _maintenanceTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected on shutdown.
            }
        }
    }

    public BrokerStats GetStats()
        => Metrics.Snapshot(
            Sessions.ConnectedCount(),
            Sessions.Count,
            Subscriptions.Count,
            Retained.Count,
            PublishRate.Snapshot());

    public IReadOnlyList<ClientSnapshot> GetClients()
        => Sessions.Snapshot()
            .Select(session => session.Snapshot(Subscriptions.GetByClient(session.ClientId).Count))
            .OrderBy(client => client.ClientId, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<SubscriptionSnapshot> GetSubscriptions()
        => Subscriptions.GetAll()
            .Select(subscription => new SubscriptionSnapshot(
                subscription.ClientId,
                subscription.Filter.Original,
                subscription.Filter.SharedGroup,
                subscription.Options.QoS.ToString(),
                subscription.Options.NoLocal,
                subscription.Options.RetainAsPublished,
                subscription.Options.RetainHandling.ToString(),
                subscription.Identifier))
            .OrderBy(subscription => subscription.ClientId, StringComparer.Ordinal)
            .ThenBy(subscription => subscription.Filter, StringComparer.Ordinal)
            .ToList();

    public IReadOnlyList<RetainedSnapshot> GetRetained()
        => Retained.Snapshot()
            .Select(message => new RetainedSnapshot(
                message.Topic,
                message.Payload.Length,
                message.QoS.ToString(),
                message.StoredAt,
                message.ExpiresAt))
            .OrderBy(message => message.Topic, StringComparer.Ordinal)
            .ToList();

    public async Task<bool> KickClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var session = Sessions.Get(clientId);
        if (session is null)
        {
            return false;
        }

        var connection = session.Connection;

        if (connection is not null)
        {
            await connection.DisconnectAsync(
                MqttReasonCodes.AdministrativeAction,
                "Disconnected by an administrator.",
                cancellationToken).ConfigureAwait(false);

            return true;
        }

        RemoveSession(session);
        return true;
    }

    public bool DeleteRetained(string topic) => Retained.Remove(topic);

    public int ClearRetained() => Retained.Count;

    public ClusterSnapshot GetCluster() => Cluster.GetSnapshot();

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);

        lock (_willGate)
        {
            foreach (var cts in _pendingWills.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _pendingWills.Clear();
        }

        foreach (var session in Sessions.Snapshot())
        {
            Subscriptions.RemoveAll(session.ClientId);
            session.Dispose();
        }

        Sessions.Clear();
        _attachGate.Dispose();
        _lifetime.Dispose();
    }

    private async Task MaintenanceLoopAsync(CancellationToken cancellationToken)
    {
        var interval = Options.SweepInterval < Options.RetransmitInterval
            ? Options.SweepInterval
            : Options.RetransmitInterval;

        if (interval < TimeSpan.FromSeconds(1))
        {
            interval = TimeSpan.FromSeconds(1);
        }

        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                Sweep(DateTimeOffset.UtcNow);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }
    }

    private void Sweep(DateTimeOffset now)
    {
        Retained.PruneExpired(now);

        foreach (var session in Sessions.Snapshot())
        {
            if (session.IsOnline)
            {
                session.RetransmitDue(now);
                continue;
            }

            if (session.ExpiresAt is { } expiry && expiry <= now)
            {
                Metrics.SessionExpired();
                RemoveSession(session);
            }
        }
    }

    private void RemoveSession(BrokerSession session)
    {
        Sessions.Remove(session.ClientId);
        Subscriptions.RemoveAll(session.ClientId);
        session.Dispose();
    }

    private void ScheduleWill(string clientId, MqttWill will, uint sessionExpiryInterval)
    {
        var delaySeconds = will.Properties.GetUInt32(MqttPropertyId.WillDelayInterval) ?? 0;

        if (sessionExpiryInterval > 0 && sessionExpiryInterval != uint.MaxValue)
        {
            delaySeconds = Math.Min(delaySeconds, sessionExpiryInterval);
        }

        var cts = new CancellationTokenSource();

        lock (_willGate)
        {
            if (_pendingWills.TryGetValue(clientId, out var existing))
            {
                existing.Cancel();
                existing.Dispose();
            }

            _pendingWills[clientId] = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                if (delaySeconds > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cts.Token).ConfigureAwait(false);
                }

                await PublishWillAsync(will, cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Session reconnected or the broker is shutting down.
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish will message for client {ClientId}", clientId);
            }
            finally
            {
                lock (_willGate)
                {
                    if (_pendingWills.TryGetValue(clientId, out var current) && ReferenceEquals(current, cts))
                    {
                        _pendingWills.Remove(clientId);
                    }
                }

                cts.Dispose();
            }
        }, CancellationToken.None);
    }

    private void CancelPendingWill(string clientId)
    {
        lock (_willGate)
        {
            if (_pendingWills.Remove(clientId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }

    private async Task PublishWillAsync(MqttWill will, CancellationToken cancellationToken)
    {
        var properties = new MqttProperties();

        foreach (var property in will.Properties.Items)
        {
            switch (property.Id)
            {
                case MqttPropertyId.PayloadFormatIndicator:
                case MqttPropertyId.MessageExpiryInterval:
                case MqttPropertyId.ContentType:
                case MqttPropertyId.ResponseTopic:
                case MqttPropertyId.CorrelationData:
                case MqttPropertyId.UserProperty:
                    properties.Add(property.Id, property.Value);
                    break;
            }
        }

        var publish = new PublishPacket
        {
            Topic = will.Topic,
            Payload = will.Payload,
            QoS = will.QoS,
            Retain = will.Retain,
            Properties = properties,
        };

        await RouteAsync(publish, sourceClientId: null, cancellationToken).ConfigureAwait(false);
    }

    private OutboundMessage BuildOutboundMessage(PublishPacket publish, DeliveryAggregate aggregate)
    {
        var properties = new MqttProperties();

        foreach (var property in publish.Properties.Items)
        {
            if (property.Id == MqttPropertyId.TopicAlias)
            {
                continue;
            }

            properties.Add(property.Id, property.Value);
        }

        foreach (var identifier in aggregate.SubscriptionIdentifiers)
        {
            properties.AddVariableByteInteger(MqttPropertyId.SubscriptionIdentifier, identifier);
        }

        DateTimeOffset? expiresAt = null;

        if (publish.Properties.GetUInt32(MqttPropertyId.MessageExpiryInterval) is { } expirySeconds and > 0)
        {
            expiresAt = DateTimeOffset.UtcNow.AddSeconds(expirySeconds);
        }

        return new OutboundMessage
        {
            Topic = publish.Topic,
            Payload = publish.Payload,
            QoS = aggregate.QoS,
            Retain = aggregate.RetainAsPublished && publish.Retain,
            Properties = properties,
            ExpiresAt = expiresAt,
        };
    }

    private static bool ResolvePersistence(ConnectPacket connect, out uint expiry)
    {
        if (connect.ProtocolVersion == MqttProtocolVersion.V500)
        {
            expiry = connect.Properties.GetUInt32(MqttPropertyId.SessionExpiryInterval) ?? 0;
            return expiry > 0;
        }

        if (connect.CleanStart)
        {
            expiry = 0;
            return false;
        }

        expiry = uint.MaxValue;
        return true;
    }

    private sealed class DeliveryAggregate
    {
        public MqttQoS QoS { get; set; } = MqttQoS.AtMostOnce;

        public bool RetainAsPublished { get; set; }

        public HashSet<int> SubscriptionIdentifiers { get; } = [];
    }
}
