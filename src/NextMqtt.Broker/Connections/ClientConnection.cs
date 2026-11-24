using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;
using System.Text;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.RateLimiting;
using NextMqtt.Broker.Security;
using NextMqtt.Broker.Sessions;
using NextMqtt.Broker.Subscriptions;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Connections;

/// <summary>
/// Owns one MQTT network connection: framing, protocol state machine,
/// delivery pump, keep-alive enforcement and clean shutdown.
/// </summary>
public sealed class ClientConnection : IClientConnection
{
    private readonly Stream _stream;
    private readonly MqttBroker _broker;
    private readonly ILogger<ClientConnection> _logger;
    private readonly MqttPacketReader _reader;
    private readonly Channel<PendingFrame> _outbound;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string _remoteAddress;
    private readonly Dictionary<ushort, string> _inboundAliases = new();
    private readonly TokenBucket? _publishBucket;
    private BrokerSession? _session;
    private Task? _writerTask;
    private MqttProtocolVersion _version = MqttProtocolVersion.V311;
    private DisconnectPacket? _disconnectPacket;
    private bool _connected;
    private bool _graceful;
    private int _closing;
    private long _lastActivity = Stopwatch.GetTimestamp();
    private ushort _maxTopicAlias;

    public ClientConnection(
        Stream stream,
        string remoteAddress,
        MqttBroker broker,
        ILogger<ClientConnection> logger)
    {
        _stream = stream;
        _remoteAddress = remoteAddress;
        _broker = broker;
        _logger = logger;
        _reader = new MqttPacketReader(
            PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: 64 * 1024, leaveOpen: true)),
            broker.Options.MaxPacketSize);

        _outbound = Channel.CreateBounded<PendingFrame>(
            new BoundedChannelOptions(1024)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait,
            });

        _maxTopicAlias = (ushort)Math.Min(broker.Options.MaxTopicAlias, ushort.MaxValue);

        var limits = broker.Options.RateLimits;
        if (limits.LimitsPublishes)
        {
            _publishBucket = new TokenBucket(
                limits.PublishPerSecondPerClient,
                limits.PublishBurst > 0 ? limits.PublishBurst : limits.PublishPerSecondPerClient);
        }
    }

    public string RemoteAddress => _remoteAddress;

    public MqttProtocolVersion ProtocolVersion => _version;

    public bool TrySend(MqttPacket packet)
    {
        if (_closing != 0)
        {
            return false;
        }

        return _outbound.Writer.TryWrite(new PendingFrame(packet, null));
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);

        try
        {
            var connect = await ReadConnectAsync(linked.Token).ConfigureAwait(false);
            if (connect is null)
            {
                return;
            }

            if (!await HandleConnectAsync(connect, linked.Token).ConfigureAwait(false))
            {
                return;
            }

            _writerTask = WriteLoopAsync(linked.Token);
            await ReadLoopAsync(linked.Token).ConfigureAwait(false);
        }
        catch (MqttProtocolException ex)
        {
            _logger.LogDebug("Protocol error from {RemoteAddress}: {Message}", _remoteAddress, ex.Message);
            await TrySendDisconnectAsync(ex.ReasonCode, ex.Message).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Keep-alive timeout, server shutdown or administrative disconnect.
        }
        catch (IOException)
        {
            // Peer reset.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unhandled connection error from {RemoteAddress}", _remoteAddress);
        }
        finally
        {
            await CleanupAsync().ConfigureAwait(false);
        }
    }

    public async Task DisconnectAsync(byte reasonCode, string reason, CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _closing, 1) != 0)
        {
            return;
        }

        if (_connected && _version == MqttProtocolVersion.V500)
        {
            await SendAsync(CreateDisconnect(reasonCode, reason), TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
        }

        _lifetime.Cancel();
    }

    private async ValueTask<ConnectPacket?> ReadConnectAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_broker.Options.ConnectTimeout);

        MqttPacket? packet;

        try
        {
            packet = await _reader.ReadAsync(MqttProtocolVersion.V500, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Timed out waiting for CONNECT from {RemoteAddress}", _remoteAddress);
            return null;
        }

        if (packet is null)
        {
            return null;
        }

        if (packet is not ConnectPacket connect)
        {
            throw MqttProtocolException.Protocol("The first packet must be CONNECT.");
        }

        return connect;
    }

    private async Task<bool> HandleConnectAsync(ConnectPacket connect, CancellationToken cancellationToken)
    {
        _version = connect.ProtocolVersion;

        if (!_broker.ConnectionLimits.TryAcquire(_remoteAddress))
        {
            _broker.Metrics.ConnectionRateLimited();
            _logger.LogDebug(
                "Connection rate limit exceeded for {RemoteAddress}",
                _remoteAddress);

            await WriteConnAckAsync(
                new ConnAckPacket
                {
                    ReasonCode = connect.ProtocolVersion == MqttProtocolVersion.V500
                        ? MqttReasonCodes.ConnectionRateExceeded
                        : MqttReasonCodes.ServerUnavailable,
                },
                cancellationToken).ConfigureAwait(false);

            return false;
        }

        string clientId;
        var assignedIdentifier = false;

        if (string.IsNullOrEmpty(connect.ClientId))
        {
            if (!_broker.Options.AllowZeroLengthClientId
                || (connect.ProtocolVersion == MqttProtocolVersion.V311 && !connect.CleanStart))
            {
                await WriteConnAckAsync(
                    new ConnAckPacket { ReasonCode = MqttReasonCodes.ClientIdentifierNotValid },
                    cancellationToken).ConfigureAwait(false);
                _broker.Metrics.ConnectionRejected();
                return false;
            }

            clientId = $"nextmqtt-{Guid.NewGuid():N}"[..17];
            assignedIdentifier = true;
        }
        else
        {
            clientId = connect.ClientId;

            if (clientId.Length > _broker.Options.MaxClientIdLength)
            {
                await WriteConnAckAsync(
                    new ConnAckPacket { ReasonCode = MqttReasonCodes.ClientIdentifierNotValid },
                    cancellationToken).ConfigureAwait(false);
                _broker.Metrics.ConnectionRejected();
                return false;
            }
        }

        var effectiveConnect = WithClientId(connect, clientId);
        var authContext = new ClientAuthenticationContext(
            clientId,
            connect.Username,
            connect.Password ?? ReadOnlyMemory<byte>.Empty,
            _remoteAddress,
            connect.ProtocolVersion);

        var auth = await _broker.AuthenticateAsync(authContext, cancellationToken).ConfigureAwait(false);

        if (!auth.Succeeded)
        {
            _broker.Metrics.AuthenticationFailed();
            _broker.Metrics.ConnectionRejected();
            await WriteConnAckAsync(
                new ConnAckPacket { ReasonCode = ToConnAckReason(auth.ReasonCode, connect.ProtocolVersion) },
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        SessionAttachResult attach;

        try
        {
            attach = await _broker.AttachSessionAsync(
                effectiveConnect,
                auth.Principal!,
                this,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to attach session for {ClientId}", clientId);
            await WriteConnAckAsync(
                new ConnAckPacket { ReasonCode = MqttReasonCodes.ServerUnavailable },
                cancellationToken).ConfigureAwait(false);
            return false;
        }

        _session = attach.Session;

        var properties = new MqttProperties();

        if (connect.ProtocolVersion == MqttProtocolVersion.V500)
        {
            properties
                .AddUInt16(MqttPropertyId.ReceiveMaximum, (ushort)_broker.Options.MaxInflightMessages)
                .AddUInt32(MqttPropertyId.MaximumPacketSize, (uint)_broker.Options.MaxPacketSize)
                .AddUInt16(MqttPropertyId.TopicAliasMaximum, _maxTopicAlias)
                .AddByte(MqttPropertyId.RetainAvailable, 1)
                .AddByte(MqttPropertyId.SharedSubscriptionAvailable, 1)
                .AddByte(MqttPropertyId.SubscriptionIdentifierAvailable, 1)
                .AddByte(MqttPropertyId.WildcardSubscriptionAvailable, 1);

            if (assignedIdentifier)
            {
                properties.AddString(MqttPropertyId.AssignedClientIdentifier, clientId);
            }
        }

        var connAck = new ConnAckPacket
        {
            SessionPresent = attach.SessionPresent,
            ReasonCode = MqttReasonCodes.Success,
            Properties = properties,
        };

        await WriteConnAckAsync(connAck, cancellationToken).ConfigureAwait(false);

        _connected = true;
        _broker.Metrics.ConnectionOpened();

        _logger.LogInformation(
            "Client {ClientId} connected from {RemoteAddress} (MQTT {Version}, session present: {SessionPresent})",
            clientId,
            _remoteAddress,
            connect.ProtocolVersion == MqttProtocolVersion.V500 ? "5.0" : "3.1.1",
            attach.SessionPresent);

        StartKeepAlive(connect.KeepAliveSeconds);
        return true;
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var packet = await _reader.ReadAsync(_version, cancellationToken).ConfigureAwait(false);

            if (packet is null)
            {
                return;
            }

            Interlocked.Exchange(ref _lastActivity, Stopwatch.GetTimestamp());

            switch (packet)
            {
                case PublishPacket publish:
                    await HandlePublishAsync(publish, cancellationToken).ConfigureAwait(false);
                    break;
                case PubAckPacket pubAck:
                    _session!.OnPubAck(pubAck.PacketId);
                    break;
                case PubRecPacket pubRec:
                    _session!.OnPubRec(pubRec.PacketId);
                    break;
                case PubRelPacket pubRel:
                    await HandlePubRelAsync(pubRel, cancellationToken).ConfigureAwait(false);
                    break;
                case PubCompPacket pubComp:
                    _session!.OnPubComp(pubComp.PacketId);
                    break;
                case SubscribePacket subscribe:
                    await HandleSubscribeAsync(subscribe, cancellationToken).ConfigureAwait(false);
                    break;
                case UnsubscribePacket unsubscribe:
                    await HandleUnsubscribeAsync(unsubscribe, cancellationToken).ConfigureAwait(false);
                    break;
                case PingReqPacket:
                    TrySend(PingRespPacket.Instance);
                    break;
                case DisconnectPacket disconnect:
                    _disconnectPacket = disconnect;
                    _graceful = true;
                    return;
                case ConnectPacket:
                    throw MqttProtocolException.Protocol("CONNECT is not allowed after the session is established.");
                case AuthPacket:
                    throw new MqttProtocolException(
                        MqttReasonCodes.BadAuthenticationMethod,
                        "Enhanced authentication is not configured.");
                default:
                    throw MqttProtocolException.Protocol("Unexpected packet type.");
            }
        }
    }

    private async Task HandlePublishAsync(PublishPacket publish, CancellationToken cancellationToken)
    {
        publish = ResolveTopicAlias(publish);

        if (!MqttTopic.IsValidTopicName(publish.Topic, out var topicError))
        {
            throw new MqttProtocolException(MqttReasonCodes.TopicNameInvalid, topicError);
        }

        var expiry = publish.Properties.GetUInt32(MqttPropertyId.MessageExpiryInterval);
        var expired = expiry == 0;
        var principal = _session!.Principal;

        if (_publishBucket is not null && !_publishBucket.TryTake())
        {
            _broker.Metrics.PublishRateLimited();
            _broker.Metrics.MessageDropped();
            _broker.Metrics.PublishReceived();

            // Acknowledge the packet so the client does not retransmit, but do
            // not route it: the quota consumed the message.
            if (publish.QoS == MqttQoS.AtMostOnce)
            {
                return;
            }

            if (publish.QoS == MqttQoS.AtLeastOnce)
            {
                _session.OnPubAck(publish.PacketId);
                TrySend(new PubAckPacket { PacketId = publish.PacketId });
                return;
            }

            TrySend(new PubRecPacket
            {
                PacketId = publish.PacketId,
                ReasonCode = _version == MqttProtocolVersion.V500
                    ? MqttReasonCodes.MessageRateTooHigh
                    : (byte)0,
            });
            return;
        }

        if (!_broker.Authorizer.CanPublish(principal, publish.Topic))
        {
            if (publish.QoS == MqttQoS.AtMostOnce)
            {
                return;
            }

            if (publish.QoS == MqttQoS.AtLeastOnce)
            {
                _session.OnPubAck(publish.PacketId);
                TrySend(new PubAckPacket
                {
                    PacketId = publish.PacketId,
                    ReasonCode = MqttReasonCodes.NotAuthorized,
                });
                return;
            }

            TrySend(new PubRecPacket
            {
                PacketId = publish.PacketId,
                ReasonCode = MqttReasonCodes.NotAuthorized,
            });
            return;
        }

        switch (publish.QoS)
        {
            case MqttQoS.AtMostOnce:
                if (!expired)
                {
                    await _broker.RouteAsync(publish, _session.ClientId, cancellationToken).ConfigureAwait(false);
                }

                break;
            case MqttQoS.AtLeastOnce:
                if (!expired)
                {
                    await _broker.RouteAsync(publish, _session.ClientId, cancellationToken).ConfigureAwait(false);
                }

                TrySend(new PubAckPacket { PacketId = publish.PacketId });
                break;
            case MqttQoS.ExactlyOnce:
                if (!expired)
                {
                    _session.RegisterInboundQos2(publish.PacketId, publish);
                }

                TrySend(new PubRecPacket { PacketId = publish.PacketId });
                break;
        }
    }

    private async Task HandlePubRelAsync(PubRelPacket pubRel, CancellationToken cancellationToken)
    {
        if (_session!.TryCompleteInboundQos2(pubRel.PacketId, out var publish) && publish is not null)
        {
            await _broker.RouteAsync(publish, _session.ClientId, cancellationToken).ConfigureAwait(false);
            TrySend(new PubCompPacket { PacketId = pubRel.PacketId });
            return;
        }

        TrySend(new PubCompPacket
        {
            PacketId = pubRel.PacketId,
            ReasonCode = _version == MqttProtocolVersion.V500
                ? MqttReasonCodes.PacketIdentifierNotFound
                : (byte)0,
        });
    }

    private async Task HandleSubscribeAsync(SubscribePacket subscribe, CancellationToken cancellationToken)
    {
        var reasonCodes = new List<byte>(subscribe.Subscriptions.Count);
        var retainedTargets = new List<Subscription>();
        var identifier = subscribe.Properties.GetVariableByteInteger(MqttPropertyId.SubscriptionIdentifier);
        var existing = _broker.Subscriptions.GetByClient(_session!.ClientId)
            .Select(subscription => subscription.Filter.Original)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var request in subscribe.Subscriptions)
        {
            if (!TopicFilter.TryParse(request.Filter, out var parsed, out _))
            {
                reasonCodes.Add(MqttReasonCodes.TopicFilterInvalid);
                continue;
            }

            if (!_broker.Authorizer.CanSubscribe(_session.Principal, request.Filter))
            {
                reasonCodes.Add(MqttReasonCodes.NotAuthorized);
                continue;
            }

            if (existing.Count >= _broker.Options.MaxSubscriptionsPerSession
                && !existing.Contains(request.Filter))
            {
                reasonCodes.Add(MqttReasonCodes.QuotaExceeded);
                continue;
            }

            var isNew = !existing.Contains(request.Filter);
            var subscription = new Subscription
            {
                ClientId = _session.ClientId,
                Filter = parsed,
                Options = request.Options,
                Identifier = identifier,
            };

            _broker.Subscriptions.AddOrReplace(subscription);
            existing.Add(request.Filter);
            _broker.Metrics.SubscriptionCreated();

            reasonCodes.Add((byte)request.Options.QoS);

            if (request.Options.RetainHandling != MqttRetainHandling.DoNotSend
                && (request.Options.RetainHandling != MqttRetainHandling.SendAtSubscribeIfNew || isNew))
            {
                retainedTargets.Add(subscription);
            }
        }

        TrySend(new SubAckPacket
        {
            PacketId = subscribe.PacketId,
            ReasonCodes = reasonCodes,
        });

        foreach (var subscription in retainedTargets)
        {
            await DeliverRetainedAsync(subscription, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleUnsubscribeAsync(UnsubscribePacket unsubscribe, CancellationToken cancellationToken)
    {
        var reasonCodes = new List<byte>(unsubscribe.Filters.Count);

        foreach (var filter in unsubscribe.Filters)
        {
            var removed = _broker.Subscriptions.Remove(_session!.ClientId, filter);
            reasonCodes.Add(removed ? MqttReasonCodes.Success : MqttReasonCodes.NoSubscriptionExisted);
        }

        TrySend(new UnsubAckPacket
        {
            PacketId = unsubscribe.PacketId,
            ReasonCodes = reasonCodes,
        });

        await Task.CompletedTask;
    }

    private async Task DeliverRetainedAsync(Subscription subscription, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var retained = _broker.Retained.Match(subscription.EffectiveFilter, now);

        foreach (var message in retained)
        {
            if (!_broker.Authorizer.CanSubscribe(_session!.Principal, message.Topic))
            {
                continue;
            }

            var properties = new MqttProperties();

            foreach (var property in message.Properties.Items)
            {
                if (property.Id != MqttPropertyId.TopicAlias)
                {
                    properties.Add(property.Id, property.Value);
                }
            }

            if (subscription.Identifier is { } identifier)
            {
                properties.AddVariableByteInteger(MqttPropertyId.SubscriptionIdentifier, identifier);
            }

            var qos = (MqttQoS)Math.Min((int)message.QoS, (int)subscription.Options.QoS);

            _session.Deliver(new OutboundMessage
            {
                Topic = message.Topic,
                Payload = message.Payload,
                QoS = qos,
                Retain = true,
                Properties = properties,
                ExpiresAt = message.ExpiresAt,
            });

            _broker.DeliveryRate.Increment();
        }

        await Task.CompletedTask;
    }

    private PublishPacket ResolveTopicAlias(PublishPacket publish)
    {
        if (_version != MqttProtocolVersion.V500)
        {
            return publish;
        }

        var alias = publish.Properties.GetUInt16(MqttPropertyId.TopicAlias);

        if (alias is null)
        {
            return publish;
        }

        if (alias == 0 || alias > _maxTopicAlias)
        {
            throw new MqttProtocolException(MqttReasonCodes.TopicAliasInvalid, "Topic alias is outside the allowed range.");
        }

        if (string.IsNullOrEmpty(publish.Topic))
        {
            if (!_inboundAliases.TryGetValue(alias.Value, out var mapped))
            {
                throw new MqttProtocolException(MqttReasonCodes.TopicAliasInvalid, "Unknown topic alias.");
            }

            return ClonePublish(publish, mapped);
        }

        _inboundAliases[alias.Value] = publish.Topic;
        return publish;
    }

    private void StartKeepAlive(ushort keepAliveSeconds)
    {
        if (keepAliveSeconds == 0)
        {
            return;
        }

        var timeout = TimeSpan.FromSeconds(keepAliveSeconds * 1.5);
        var checkInterval = TimeSpan.FromSeconds(Math.Max(1, keepAliveSeconds / 2.0));

        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(checkInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(_lifetime.Token).ConfigureAwait(false))
                {
                    var idle = Stopwatch.GetElapsedTime(Interlocked.Read(ref _lastActivity));
                    if (idle > timeout)
                    {
                        _logger.LogDebug("Client {ClientId} exceeded its keep-alive interval", _session?.ClientId);
                        await DisconnectAsync(
                            MqttReasonCodes.KeepAliveTimeout,
                            "Keep-alive interval exceeded.",
                            CancellationToken.None).ConfigureAwait(false);
                        return;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Connection closed.
            }
        }, CancellationToken.None);
    }

    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var buffer = new ArrayBufferWriter<byte>(256);
                MqttPacketWriter.Write(buffer, frame.Packet, _version);
                await _stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                _broker.Metrics.AddBytesSent(buffer.WrittenCount);
                frame.Completion?.TrySetResult();
            }
        }
        catch (OperationCanceledException)
        {
            // Connection closing.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Write loop ended for {RemoteAddress}", _remoteAddress);
        }
    }

    private async Task WriteConnAckAsync(ConnAckPacket connAck, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(64);
        MqttPacketWriter.Write(buffer, connAck, _version);
        await _stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _broker.Metrics.AddBytesSent(buffer.WrittenCount);
    }

    private async Task<bool> SendAsync(MqttPacket packet, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        if (!_outbound.Writer.TryWrite(new PendingFrame(packet, completion)))
        {
            return false;
        }

        try
        {
            await completion.Task.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task TrySendDisconnectAsync(byte reasonCode, string reason)
    {
        if (!_connected || _version != MqttProtocolVersion.V500)
        {
            return;
        }

        await SendAsync(CreateDisconnect(reasonCode, reason), TimeSpan.FromSeconds(1), CancellationToken.None)
            .ConfigureAwait(false);
    }

    private static DisconnectPacket CreateDisconnect(byte reasonCode, string reason)
    {
        var properties = new MqttProperties();

        if (!string.IsNullOrEmpty(reason))
        {
            var trimmed = reason.Length > 512 ? reason[..512] : reason;
            properties.AddString(MqttPropertyId.ReasonString, trimmed);
        }

        return new DisconnectPacket { ReasonCode = reasonCode, Properties = properties };
    }

    private async Task CleanupAsync()
    {
        _outbound.Writer.TryComplete();

        if (_writerTask is not null)
        {
            try
            {
                await _writerTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Writer already failed or timed out.
            }
        }

        if (_session is not null)
        {
            await _broker.OnConnectionClosedAsync(
                _session,
                this,
                _disconnectPacket,
                _graceful,
                CancellationToken.None).ConfigureAwait(false);
        }

        _lifetime.Cancel();
        _lifetime.Dispose();
        _stream.Dispose();
    }

    private static byte ToConnAckReason(byte reasonCode, MqttProtocolVersion version)
    {
        if (version == MqttProtocolVersion.V500)
        {
            return reasonCode;
        }

        return reasonCode switch
        {
            MqttReasonCodes.UnsupportedProtocolVersion => 0x01,
            MqttReasonCodes.ClientIdentifierNotValid => 0x02,
            MqttReasonCodes.BadUserNameOrPassword => 0x04,
            MqttReasonCodes.NotAuthorized => 0x05,
            MqttReasonCodes.ServerUnavailable => 0x03,
            _ => 0x03,
        };
    }

    private static ConnectPacket WithClientId(ConnectPacket connect, string clientId)
    {
        return new ConnectPacket
        {
            ProtocolVersion = connect.ProtocolVersion,
            CleanStart = connect.CleanStart,
            KeepAliveSeconds = connect.KeepAliveSeconds,
            ClientId = clientId,
            Properties = connect.Properties,
            Will = connect.Will,
            Username = connect.Username,
            Password = connect.Password,
        };
    }

    private static PublishPacket ClonePublish(PublishPacket publish, string topic)
    {
        return new PublishPacket
        {
            Topic = topic,
            Payload = publish.Payload,
            QoS = publish.QoS,
            Retain = publish.Retain,
            Duplicate = publish.Duplicate,
            PacketId = publish.PacketId,
            Properties = publish.Properties,
        };
    }

    private readonly record struct PendingFrame(MqttPacket Packet, TaskCompletionSource? Completion);
}
