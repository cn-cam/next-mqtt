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
/// <remarks>
/// 读写各跑一个循环，中间用有界通道衔接：读循环只负责解析，慢客户端不会阻塞
/// 路由；写循环独占套接字，保证同一时刻只有一个包在途。任何一侧出错都汇到
/// 同一个清理入口，断开、发遗嘱、摘会话只做一次。
/// </remarks>
public sealed class ClientConnection : IClientConnection, IRateScalable
{
    /// <summary>Transport stream: TCP, TLS or WebSocket, owned by this connection.</summary>
    private readonly Stream _stream;
    /// <summary>Broker that owns sessions, routing and metrics.</summary>
    private readonly MqttBroker _broker;
    /// <summary>Connection-scoped logger; logging must never fail the connection.</summary>
    private readonly ILogger<ClientConnection> _logger;
    /// <summary>Frame reader over the stream, created with the configured packet size limit.</summary>
    private readonly MqttPacketReader _reader;
    /// <summary>Bounded queue between routing and the write loop; a full queue means a slow client.</summary>
    private readonly Channel<PendingFrame> _outbound;
    /// <summary>Cancelled once during shutdown; stops both loops.</summary>
    private readonly CancellationTokenSource _lifetime = new();
    /// <summary>Peer address as reported by the transport, used for rate limits and logs.</summary>
    private readonly string _remoteAddress;
    /// <summary>Aliases the client registered for its own publishes, valid for this connection only.</summary>
    private readonly Dictionary<ushort, string> _inboundAliases = new();
    /// <summary>Per-client publish bucket, or null when no publish rate is configured.</summary>
    private readonly TokenBucket? _publishBucket;

    /// <summary>Topics this client has been assigned an outbound alias for.</summary>
    /// <remarks>别名只在当前连接有效，重连必须清空，规范不允许跨连接复用。</remarks>
    private readonly Dictionary<string, ushort> _outboundAliases = new(StringComparer.Ordinal);

    /// <summary>Authentication method negotiated by CONNECT, when enhanced auth was used.</summary>
    /// <remarks>重认证时客户端可以省略方法名，这时沿用它继续。</remarks>
    private string? _authenticationMethod;

    /// <summary>Largest alias the client accepts, from its CONNECT properties.</summary>
    /// <remarks>为 0 表示客户端不支持别名，所有 PUBLISH 都要带完整主题。</remarks>
    private ushort _clientTopicAliasMax;

    /// <summary>Next alias to hand out; aliases start at 1.</summary>
    /// <remarks>从 1 递增，达到客户端上限后不再分配新别名，改为重发完整主题。</remarks>
    private ushort _nextOutboundAlias = 1;
    /// <summary>Configured publish rate, kept so a cluster rescale can rebuild the bucket.</summary>
    private readonly int _publishRate;
    /// <summary>Configured publish burst, kept for the same reason.</summary>
    private readonly int _publishBurst;
    /// <summary>Live node count applied to this bucket; one until the first rescale.</summary>
    private int _clusterSize = 1;
    /// <summary>Attached session, null before CONNECT and after cleanup.</summary>
    private BrokerSession? _session;
    /// <summary>Write loop task, awaited during cleanup so no frame is lost.</summary>
    private Task? _writerTask;
    /// <summary>Protocol level from CONNECT; 3.1.1 until the packet is parsed.</summary>
    private MqttProtocolVersion _version = MqttProtocolVersion.V311;
    /// <summary>DISCONNECT sent by the client, used for will and expiry decisions.</summary>
    private DisconnectPacket? _disconnectPacket;
    /// <summary>True once CONNACK was sent.</summary>
    private bool _connected;
    /// <summary>True when the peer sent DISCONNECT, which suppresses the will.</summary>
    private bool _graceful;
    /// <summary>Set with Interlocked so cleanup runs exactly once.</summary>
    private int _closing;
    /// <summary>Last packet timestamp on the monotonic clock, used by keep-alive.</summary>
    private long _lastActivity = Stopwatch.GetTimestamp();
    /// <summary>Inbound alias budget taken from broker options.</summary>
    private ushort _maxTopicAlias;

    /// <summary>Creates the per-connection state machine.</summary>
    /// <remarks>限流桶与读取上限在这里定下来，连接期间不再变动。</remarks>
    /// <param name="stream">Transport stream (TCP, TLS or WebSocket).</param>
    /// <param name="remoteAddress">Peer address, port included.</param>
    /// <param name="broker">Broker that owns the session.</param>
    /// <param name="logger">Connection logger.</param>
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
        _clusterSize = Math.Max(1, broker.ClusterSize);

        if (limits.LimitsPublishes)
        {
            _publishRate = limits.PublishPerSecondPerClient;
            _publishBurst = limits.PublishBurst > 0 ? limits.PublishBurst : limits.PublishPerSecondPerClient;
            _publishBucket = new TokenBucket(
                Math.Max(1, _publishRate / _clusterSize),
                Math.Max(1, _publishBurst / _clusterSize));
        }
    }

    /// <summary>
    /// Rescales the per-client publish quota when cluster membership changes.
    /// </summary>
    /// <remarks>每个节点只放行总配额的 1/N，避免多节点叠加放大限额。</remarks>
    public void SetClusterSize(int nodes)
    {
        var size = Math.Max(1, nodes);

        if (size == _clusterSize || _publishBucket is null)
        {
            return;
        }

        _clusterSize = size;
        _publishBucket.Reconfigure(
            Math.Max(1.0, (double)_publishRate / size),
            Math.Max(1.0, (double)_publishBurst / size));
    }

    /// <summary>Peer address reported by the transport.</summary>
    /// <remarks>用于按 IP 限流和日志，格式由传输层决定，可能是 IPv6。</remarks>
    public string RemoteAddress => _remoteAddress;

    /// <summary>Protocol level negotiated by CONNECT.</summary>
    /// <remarks>决定下发报文能否编码 MQTT 5 属性。</remarks>
    public MqttProtocolVersion ProtocolVersion => _version;

    /// <summary>Queues a packet without blocking; false when closing.</summary>
    /// <remarks>返回 false 说明连接正在关闭，路由层应立即停止投递。</remarks>
    /// <param name="packet">Packet to enqueue.</param>
    public bool TrySend(MqttPacket packet)
    {
        if (_closing != 0)
        {
            return false;
        }

        return _outbound.Writer.TryWrite(new PendingFrame(packet, null));
    }

    /// <summary>Reads CONNECT, then runs the read loop until the peer leaves.</summary>
    /// <remarks>先启动写循环再读；任何异常都汇到同一个清理路径。</remarks>
    /// <param name="cancellationToken">Stops the connection on shutdown.</param>
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

    /// <summary>Sends DISCONNECT on MQTT 5 and closes the transport.</summary>
    /// <remarks>3.1.1 没有原因码，直接关套接字。</remarks>
    /// <param name="reasonCode">Reason code shown to the client.</param>
    /// <param name="reason">Explanation used in local logs.</param>
    /// <param name="cancellationToken">Cancels the close.</param>
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

    /// <summary>Waits for the first packet under the connect timeout; null means timeout or clean close.</summary>
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

    /// <summary>Validates CONNECT, authenticates, attaches the session and writes CONNACK; false means the client was refused.</summary>
    private async Task<bool> HandleConnectAsync(ConnectPacket connect, CancellationToken cancellationToken)
    {
        _version = connect.ProtocolVersion;

        if (connect.ProtocolVersion == MqttProtocolVersion.V500
            && connect.Properties.GetUInt16(MqttPropertyId.TopicAliasMaximum) is > 0 and var aliasMaximum)
        {
            _clientTopicAliasMax = aliasMaximum;
        }

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

        AuthenticationResult auth;

        if (connect.Properties.GetString(MqttPropertyId.AuthenticationMethod) is { Length: > 0 } method)
        {
            _authenticationMethod = method;
            var enhanced = await RunEnhancedAuthAsync(connect, method, cancellationToken).ConfigureAwait(false);

            if (!enhanced.Succeeded)
            {
                _broker.Metrics.AuthenticationFailed();
                _broker.Metrics.ConnectionRejected();
                await WriteConnAckAsync(
                    new ConnAckPacket { ReasonCode = enhanced.ReasonCode },
                    cancellationToken).ConfigureAwait(false);
                return false;
            }

            auth = AuthenticationResult.Success(enhanced.Principal!);
        }
        else
        {
            var authContext = new ClientAuthenticationContext(
                clientId,
                connect.Username,
                connect.Password ?? ReadOnlyMemory<byte>.Empty,
                _remoteAddress,
                connect.ProtocolVersion);

            auth = await _broker.AuthenticateAsync(authContext, cancellationToken).ConfigureAwait(false);
        }

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

    /// <summary>Dispatches packets after CONNECT until the peer leaves or violates the protocol.</summary>
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
                case AuthPacket authPacket:
                    await HandleAuthAsync(authPacket, cancellationToken).ConfigureAwait(false);
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
                default:
                    throw MqttProtocolException.Protocol("Unexpected packet type.");
            }
        }
    }

    /// <summary>Applies alias, syntax, ACL, rate limits and rules to one inbound PUBLISH.</summary>
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

        if ((_publishBucket is not null && !_publishBucket.TryTake())
            || !_broker.TopicQuotas.TryAcquire(publish.Topic))
        {
            // Count the message as received before marking it dropped, so an
            // observer that sees the drop counter also sees the receipt.
            _broker.Metrics.PublishReceived();
            _broker.Metrics.PublishRateLimited();
            _broker.Metrics.MessageDropped();

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

    /// <summary>Completes the inbound QoS 2 handshake and routes the stored message.</summary>
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

    /// <summary>Applies ACL and quota checks to SUBSCRIBE, then answers with SUBACK and retained messages.</summary>
    private async Task HandleSubscribeAsync(SubscribePacket subscribe, CancellationToken cancellationToken)
    {
        var reasonCodes = new List<byte>(subscribe.Subscriptions.Count);
        var retainedTargets = new List<Subscription>();
        var identifier = subscribe.Properties.GetVariableByteInteger(MqttPropertyId.SubscriptionIdentifier);
        var clientSubscriptions = _broker.Subscriptions.GetByClient(_session!.ClientId);
        var existing = clientSubscriptions
            .Select(subscription => subscription.Filter.Original)
            .ToHashSet(StringComparer.Ordinal);
        var identified = clientSubscriptions.Count(subscription => subscription.Identifier is not null);
        var identifierLimit = _broker.Options.MaxSubscriptionIdentifiersPerClient;

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
            var hadIdentifier = !isNew && clientSubscriptions.Any(subscription =>
                subscription.Identifier is not null
                && string.Equals(subscription.Filter.Original, request.Filter, StringComparison.Ordinal));

            if (identifier is not null && identifierLimit > 0 && !hadIdentifier && identified >= identifierLimit)
            {
                reasonCodes.Add(MqttReasonCodes.QuotaExceeded);
                continue;
            }

            if (identifier is not null && !hadIdentifier)
            {
                identified++;
            }

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

    /// <summary>Removes the requested filters and answers with per-filter reason codes.</summary>
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

    /// <summary>Sends retained messages for a new subscription, honouring retain handling and granted QoS.</summary>
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

    /// <summary>Expands an inbound alias or records a new one; an unknown alias ends the connection.</summary>
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

    /// <summary>Starts the 1.5x keep-alive watchdog; zero disables it.</summary>
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

    /// <summary>Runs the enhanced authentication exchange before CONNACK.</summary>
    /// <remarks>服务端可以连续发送 AUTH 0x18 挑战，客户端必须用 AUTH 回应；最多 4 轮。</remarks>
    /// <param name="connect">CONNECT packet carrying the method and first data block.</param>
    /// <param name="method">Authentication method name.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    /// <returns>Final result; a challenge is never returned.</returns>
    private async Task<EnhancedAuthResult> RunEnhancedAuthAsync(
        ConnectPacket connect,
        string method,
        CancellationToken cancellationToken)
    {
        var provider = _broker.GetEnhancedAuthenticator(method);

        if (provider is null)
        {
            return EnhancedAuthResult.Failure(MqttReasonCodes.BadAuthenticationMethod);
        }

        var context = new EnhancedAuthContext(
            connect.ClientId,
            connect.Username,
            _remoteAddress,
            connect.Properties.GetBinary(MqttPropertyId.AuthenticationData) ?? []);

        var result = await provider.AuthenticateAsync(context, cancellationToken).ConfigureAwait(false);

        for (var round = 0; !result.Succeeded && result.Challenge is not null && round < 4; round++)
        {
            await WriteAuthAsync(
                MqttReasonCodes.ContinueAuthentication,
                method,
                result.Challenge,
                cancellationToken).ConfigureAwait(false);

            var packet = await _reader.ReadAsync(_version, cancellationToken).ConfigureAwait(false);

            if (packet is not AuthPacket auth)
            {
                return EnhancedAuthResult.Failure(MqttReasonCodes.ProtocolError);
            }

            var roundMethod = auth.Properties.GetString(MqttPropertyId.AuthenticationMethod);

            if (roundMethod is not null && !string.Equals(roundMethod, method, StringComparison.Ordinal))
            {
                return EnhancedAuthResult.Failure(MqttReasonCodes.BadAuthenticationMethod);
            }

            context = context with
            {
                Data = auth.Properties.GetBinary(MqttPropertyId.AuthenticationData) ?? [],
            };

            result = await provider.AuthenticateAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return result.Challenge is not null && !result.Succeeded
            ? EnhancedAuthResult.Failure(MqttReasonCodes.NotAuthorized)
            : result;
    }

    /// <summary>Writes an AUTH packet directly, before the write loop starts.</summary>
    /// <remarks>CONNACK 之前写循环还没起来，只能直接写流。</remarks>
    /// <param name="reasonCode">0x18 challenge or a final code.</param>
    /// <param name="method">Authentication method echoed to the client.</param>
    /// <param name="data">Challenge or result bytes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    private async Task WriteAuthAsync(
        byte reasonCode,
        string method,
        byte[] data,
        CancellationToken cancellationToken)
    {
        var properties = new MqttProperties().AddString(MqttPropertyId.AuthenticationMethod, method);

        if (data.Length > 0)
        {
            properties.AddBinary(MqttPropertyId.AuthenticationData, data);
        }

        var buffer = new ArrayBufferWriter<byte>(64);
        MqttPacketWriter.Write(
            buffer,
            new AuthPacket { ReasonCode = reasonCode, Properties = properties },
            MqttProtocolVersion.V500);

        await _stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _broker.Metrics.AddBytesSent(buffer.WrittenCount);
    }

    /// <summary>Handles a client AUTH packet for re-authentication on a live connection.</summary>
    /// <remarks>认证成功回 0x00；失败先发错误原因码再断开，避免留下半开连接。</remarks>
    /// <param name="auth">AUTH packet from the client.</param>
    /// <param name="cancellationToken">Cancels the exchange.</param>
    private async Task HandleAuthAsync(AuthPacket auth, CancellationToken cancellationToken)
    {
        if (_version != MqttProtocolVersion.V500)
        {
            return;
        }

        var method = auth.Properties.GetString(MqttPropertyId.AuthenticationMethod) ?? _authenticationMethod;
        var provider = method is null ? null : _broker.GetEnhancedAuthenticator(method);

        if (provider is null || _session is null)
        {
            TrySend(new AuthPacket
            {
                ReasonCode = MqttReasonCodes.BadAuthenticationMethod,
                Properties = new MqttProperties(),
            });
            return;
        }

        var result = await provider.AuthenticateAsync(
            new EnhancedAuthContext(
                _session.ClientId,
                _session.Principal.Username,
                _remoteAddress,
                auth.Properties.GetBinary(MqttPropertyId.AuthenticationData) ?? []),
            cancellationToken).ConfigureAwait(false);

        if (result.Succeeded)
        {
            TrySend(new AuthPacket
            {
                ReasonCode = MqttReasonCodes.Success,
                Properties = new MqttProperties().AddString(MqttPropertyId.AuthenticationMethod, method!),
            });
            return;
        }

        if (result.Challenge is not null)
        {
            TrySend(new AuthPacket
            {
                ReasonCode = MqttReasonCodes.ContinueAuthentication,
                Properties = new MqttProperties()
                    .AddString(MqttPropertyId.AuthenticationMethod, method!)
                    .AddBinary(MqttPropertyId.AuthenticationData, result.Challenge),
            });
            return;
        }

        TrySend(new AuthPacket
        {
            ReasonCode = result.ReasonCode,
            Properties = new MqttProperties().AddString(MqttPropertyId.AuthenticationMethod, method!),
        });
        await DisconnectAsync(result.ReasonCode, "Re-authentication failed", cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Serialises queued frames onto the stream and completes their senders.</summary>
    private async Task WriteLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var frame in _outbound.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var buffer = new ArrayBufferWriter<byte>(256);
                MqttPacketWriter.Write(buffer, ApplyOutboundAlias(frame.Packet), _version);
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

    /// <summary>Rewrites a PUBLISH to use the client's outbound topic alias budget.</summary>
    /// <remarks>
    /// 第一次发送某主题时带上完整主题和新分配的别名；之后再发同一主题时主题留空，只带别名。
    /// 别名用尽后新主题继续发送完整主题，符合 MQTT 5 规范 3.3.2.3.4。
    /// </remarks>
    /// <param name="packet">Packet queued for this connection.</param>
    /// <returns>Original packet, or a PUBLISH rewritten for aliasing.</returns>
    private MqttPacket ApplyOutboundAlias(MqttPacket packet)
    {
        if (_clientTopicAliasMax == 0 || packet is not PublishPacket publish || publish.Topic.Length == 0)
        {
            return packet;
        }

        if (_outboundAliases.TryGetValue(publish.Topic, out var alias))
        {
            return Rewrite(publish, string.Empty, alias);
        }

        if (_outboundAliases.Count >= _clientTopicAliasMax)
        {
            return packet;
        }

        alias = _nextOutboundAlias++;
        _outboundAliases[publish.Topic] = alias;
        return Rewrite(publish, publish.Topic, alias);
    }

    /// <summary>Copies a PUBLISH, optionally blanking the topic and adding an alias.</summary>
    /// <remarks>属性逐条复制，不能和原报文共用可变集合。</remarks>
    /// <param name="publish">Original packet.</param>
    /// <param name="topic">Topic to send (empty for alias-only).</param>
    /// <param name="alias">Alias to attach.</param>
    /// <returns>Rewritten packet with copied properties.</returns>
    private static PublishPacket Rewrite(PublishPacket publish, string topic, ushort alias)
    {
        var properties = new MqttProperties();

        foreach (var property in publish.Properties.Items)
        {
            properties.Add(property.Id, property.Value);
        }

        properties.AddUInt16(MqttPropertyId.TopicAlias, alias);

        return new PublishPacket
        {
            Topic = topic,
            Payload = publish.Payload,
            QoS = publish.QoS,
            Retain = publish.Retain,
            Duplicate = publish.Duplicate,
            PacketId = publish.PacketId,
            Properties = properties,
        };
    }

    /// <summary>Writes CONNACK directly, because the write loop does not exist yet.</summary>
    private async Task WriteConnAckAsync(ConnAckPacket connAck, CancellationToken cancellationToken)
    {
        var buffer = new ArrayBufferWriter<byte>(64);
        MqttPacketWriter.Write(buffer, connAck, _version);
        await _stream.WriteAsync(buffer.WrittenMemory, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        _broker.Metrics.AddBytesSent(buffer.WrittenCount);
    }

    /// <summary>Queues a packet and waits for the write loop, bounded by a timeout.</summary>
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

    /// <summary>Sends DISCONNECT on a best-effort basis; failures are ignored.</summary>
    private async Task TrySendDisconnectAsync(byte reasonCode, string reason)
    {
        if (!_connected || _version != MqttProtocolVersion.V500)
        {
            return;
        }

        await SendAsync(CreateDisconnect(reasonCode, reason), TimeSpan.FromSeconds(1), CancellationToken.None)
            .ConfigureAwait(false);
    }

    /// <summary>Builds a DISCONNECT carrying a reason string for MQTT 5 peers.</summary>
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

    /// <summary>Single exit path: detach the session, publish the will, release the transport.</summary>
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

    /// <summary>Maps a 5.0 reason code to the closest 3.1.1 CONNACK return code.</summary>
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

    /// <summary>Returns a CONNECT copy carrying the generated client identifier.</summary>
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

    /// <summary>Returns a copy with a resolved topic, sharing payload and properties.</summary>
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

    /// <summary>One queued frame plus the optional waiter released after it is written.</summary>
    private readonly record struct PendingFrame(MqttPacket Packet, TaskCompletionSource? Completion);
}
