using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Diagnostics;
using NextMqtt.Broker.Options;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Clustering;

/// <summary>
/// Gossip membership plus signed HTTP forwarding. Every node advertises its
/// normal subscription filters; a publisher forwards only to nodes with a
/// matching filter. Retained publications are replicated to all live nodes.
/// </summary>
/// <remarks>
/// Shared subscriptions are evaluated per node. This trade-off keeps the
/// transport stateless; see docs/architecture.md for the rationale.
/// </remarks>
public sealed class ClusterRouter : IClusterRouter
{
    private readonly ClusterOptions _options;
    private readonly ILogger<ClusterRouter> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, Member> _members = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _seenMessages = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _lifetime = new();
    private Task? _loop;
    private MqttBroker? _broker;
    private Member? _self;
    private long _publishesSent;
    private long _publishesReceived;
    private int _stopped;

    /// <summary>Creates the router; it stays idle until <see cref="StartAsync"/> is called.</summary>
    /// <param name="options">Cluster settings including node id, seeds and secret.</param>
    /// <param name="logger">Logger for heartbeat and forwarding failures.</param>
    /// <param name="httpClient">Client used for heartbeats and publish forwarding.</param>
    public ClusterRouter(
        ClusterOptions options,
        ILogger<ClusterRouter> logger,
        HttpClient httpClient)
    {
        _options = options;
        _logger = logger;
        _httpClient = httpClient;
    }

    /// <summary>True when a cluster secret and node id are configured.</summary>
    /// <remarks>关闭时路由退化为空操作，也不会启动心跳。</remarks>
    public bool Enabled => _options.Enabled;

    /// <summary>Shared secret used to sign requests to peers.</summary>
    /// <remarks>只在本机参与 HMAC；泄露等于整个集群失守。</remarks>
    public string Secret => _options.Secret;

    /// <summary>Gives the router the broker used for routing inbound forwards.</summary>
    /// <remarks>必须在 StartAsync 之前调用，否则收到的转发会被丢掉。</remarks>
    /// <param name="broker">Broker that owns the router.</param>
    public void AttachBroker(MqttBroker broker) => _broker = broker;

    /// <summary>Starts the heartbeat loop when clustering is enabled.</summary>
    /// <remarks>集群关闭时立即返回，不占用后台线程。</remarks>
    /// <param name="cancellationToken">Cancels startup.</param>
    public ValueTask StartAsync(CancellationToken cancellationToken = default)
    {
        if (!Enabled || _loop is not null)
        {
            return ValueTask.CompletedTask;
        }

        if (string.IsNullOrWhiteSpace(_options.AdvertiseUrl))
        {
            throw new InvalidOperationException("Cluster:AdvertiseUrl must be configured when clustering is enabled.");
        }

        if (_options.Secret.Length < 16)
        {
            throw new InvalidOperationException("Cluster:Secret must contain at least 16 characters.");
        }

        if (!Uri.TryCreate(_options.AdvertiseUrl, UriKind.Absolute, out var advertiseUri)
            || (advertiseUri.Scheme != Uri.UriSchemeHttp && advertiseUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Cluster:AdvertiseUrl must be an absolute http(s) URL.");
        }

        var nodeId = string.IsNullOrWhiteSpace(_options.NodeId)
            ? $"{Environment.MachineName.ToLowerInvariant()}-{Random.Shared.Next(0x100000, 0xFFFFFF):x6}"
            : _options.NodeId;

        _self = new Member(nodeId, _options.AdvertiseUrl.TrimEnd('/'), DateTimeOffset.UtcNow, []);
        _members[nodeId] = _self;

        _logger.LogInformation(
            "Cluster node {NodeId} starting with {SeedCount} seed(s), advertise URL {AdvertiseUrl}",
            nodeId,
            _options.Seeds.Count,
            _self.AdvertiseUrl);

        _loop = Task.Run(() => HeartbeatLoopAsync(_lifetime.Token), CancellationToken.None);
        return ValueTask.CompletedTask;
    }

    /// <summary>Stops the heartbeat loop and tries to announce the departure.</summary>
    /// <remarks>尽力而为；对端不可达只记日志，不阻塞关闭。</remarks>
    public async ValueTask StopAsync()
    {
        if (Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        _lifetime.Cancel();

        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected.
            }
        }

        _lifetime.Dispose();
    }

    /// <summary>Projects the membership table for the cluster API.</summary>
    /// <remarks>每次请求现算，不缓存状态，避免把死节点显示成活的。</remarks>
    public ClusterSnapshot GetSnapshot()
    {
        if (!Enabled || _self is null)
        {
            return new ClusterSnapshot(_options.NodeId, "standalone", []);
        }

        var now = DateTimeOffset.UtcNow;

        var nodes = _members.Values
            .Select(member => new ClusterNodeSnapshot(
                member.NodeId,
                member.AdvertiseUrl,
                member.Status(now, TimeSpan.FromSeconds(_options.NodeTimeoutSeconds)),
                member.LastSeen,
                member.NodeId == _self.NodeId))
            .OrderBy(node => node.NodeId, StringComparer.Ordinal)
            .ToList();

        return new ClusterSnapshot(_self.NodeId, "clustered", nodes);
    }

    /// <summary>Forwards a publish to peers that advertised a matching filter.</summary>
    /// <remarks>尽力而为：失败只记日志，不影响本地投递。</remarks>
    /// <param name="publish">Publish already accepted locally.</param>
    /// <param name="cancellationToken">Cancels the fan-out.</param>
    public async ValueTask RouteAsync(PublishPacket publish, string messageId, CancellationToken cancellationToken = default)
    {
        if (!Enabled || _self is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(_options.NodeTimeoutSeconds);
        var peers = _members.Values
            .Where(member => member.NodeId != _self.NodeId)
            .Where(member => member.Status(now, timeout) == "alive")
            .ToList();

        var normalTargets = peers
            .Where(member => publish.Retain || member.MatchesNormal(publish.Topic))
            .ToList();

        var sharedTargets = new List<(Member Member, string Group)>();

        foreach (var group in SharedGroups(publish.Topic, peers))
        {
            var candidates = SharedCandidates(publish.Topic, group, peers);

            if (candidates.Count == 0)
            {
                continue;
            }

            var chosen = candidates[StableIndex(messageId, group, candidates.Count)];

            if (string.Equals(chosen, _self.NodeId, StringComparison.Ordinal))
            {
                continue;
            }

            var member = peers.FirstOrDefault(peer => string.Equals(peer.NodeId, chosen, StringComparison.Ordinal));
            if (member is not null)
            {
                sharedTargets.Add((member, group));
            }
        }

        if (normalTargets.Count == 0 && sharedTargets.Count == 0)
        {
            return;
        }

        var message = new ClusterPublishMessage(
            messageId,
            _self.NodeId,
            0,
            publish.Topic,
            Convert.ToBase64String(publish.Payload),
            (int)publish.QoS,
            publish.Retain,
            publish.Duplicate,
            publish.Properties.Items.Select(EncodeProperty).ToArray(),
            null,
            publish.Retain ? DateTimeOffset.UtcNow.ToUnixTimeSeconds() : null);

        var sends = normalTargets
            .Select(member => SendPublishAsync(member, message, cancellationToken))
            .Concat(sharedTargets.Select(target => SendPublishAsync(
                target.Member,
                message with { SharedGroup = target.Group },
                cancellationToken)))
            .ToList();

        await Task.WhenAll(sends).ConfigureAwait(false);
    }

    /// <summary>Copies the membership table for gossip responses.</summary>
    /// <remarks>心跳响应携带本地视图，供对端合并与发现。</remarks>
    public MemberSnapshot[] SnapshotMembers()
    {
        if (_self is null)
        {
            return [];
        }

        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(_options.NodeTimeoutSeconds);

        return _members.Values
            .Select(member => new MemberSnapshot(
                member.NodeId,
                member.AdvertiseUrl,
                member.Status(now, timeout),
                member.LastSeen.ToUnixTimeSeconds(),
                member.RawFilters))
            .ToArray();
    }

    /// <summary>Handles a peer heartbeat and returns the local membership view.</summary>
    /// <remarks>同时刷新 lastSeen，把 suspect 节点拉回 alive。</remarks>
    /// <param name="heartbeat">Heartbeat received from the peer.</param>
    /// <param name="cancellationToken">Cancels the response.</param>
    public ValueTask<ClusterHeartbeatResponse> ProcessHeartbeatAsync(
        ClusterHeartbeatRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_self is null)
        {
            return ValueTask.FromResult(new ClusterHeartbeatResponse(
                _options.NodeId,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                []));
        }

        MergeMember(request.NodeId, request.AdvertiseUrl, request.Filters, DateTimeOffset.UtcNow);

        var response = new ClusterHeartbeatResponse(
            _self.NodeId,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            SnapshotMembers().Select(member => new ClusterMemberDto(
                member.NodeId,
                member.AdvertiseUrl,
                member.Status,
                member.LastSeenUnix,
                member.Filters)).ToArray());

        PruneDeadMembers(DateTimeOffset.UtcNow);
        return ValueTask.FromResult(response);
    }

    /// <summary>Routes a publish received from a peer, rejecting duplicates.</summary>
    /// <remarks>按 messageId 去重；共享专用消息不去重，因为它与普通扇出共用同一个 id。</remarks>
    /// <param name="message">Forwarded message.</param>
    /// <param name="cancellationToken">Cancels routing.</param>
    /// <returns><c>true</c> when accepted; <c>false</c> for duplicates.</returns>
    public async ValueTask<bool> ProcessPublishAsync(
        ClusterPublishMessage message,
        CancellationToken cancellationToken = default)
    {
        if (!Enabled || _broker is null || _self is null)
        {
            return false;
        }

        if (string.IsNullOrEmpty(message.MessageId) || message.OriginNodeId == _self.NodeId)
        {
            return false;
        }

        var sharedOnly = message.SharedGroup is { Length: > 0 };

        // A targeted shared message reuses the message id of the normal fan-out,
        // so it must not be rejected as a duplicate of that fan-out.
        if (!sharedOnly && !_seenMessages.TryAdd(message.MessageId, DateTimeOffset.UtcNow))
        {
            return false;
        }

        if (message.Qos is < 0 or > 2 || !MqttTopic.IsValidTopicName(message.Topic, out _))
        {
            return false;
        }

        byte[] payload;

        try
        {
            payload = Convert.FromBase64String(message.Payload);
        }
        catch (FormatException)
        {
            return false;
        }

        var properties = new MqttProperties();

        foreach (var property in message.Properties)
        {
            try
            {
                properties.Add((MqttPropertyId)property.Id, DecodeProperty(property));
            }
            catch (Exception)
            {
                // Ignore properties that cannot be represented; the payload and
                // topic remain authoritative.
            }
        }

        var publish = new PublishPacket
        {
            Topic = message.Topic,
            Payload = payload,
            QoS = (MqttQoS)message.Qos,
            Retain = message.Retain,
            Duplicate = message.Duplicate,
            Properties = properties,
        };

        Interlocked.Increment(ref _publishesReceived);

        if (sharedOnly)
        {
            await _broker.RouteSharedFromClusterAsync(publish, message.SharedGroup!, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (message.RetainedAtUnix is { } retainedUnix && publish.Retain)
        {
            await _broker.RouteFromClusterRetainedAsync(
                publish,
                DateTimeOffset.FromUnixTimeSeconds(retainedUnix),
                cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _broker.RouteFromClusterAsync(publish, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    /// <summary>Removes a node that announced its departure.</summary>
    /// <remarks>主动离开立即摘除，不必等超时。</remarks>
    /// <param name="nodeId">Node identifier.</param>
    /// <summary>Shared groups with at least one local subscriber for this topic.</summary>
    /// <remarks>本地没有的组也要从远端成员收集，否则源节点根本不知道要转发。</remarks>
    /// <param name="topic">Topic being published.</param>
    private List<string> SharedGroups(string topic, List<Member> peers)
    {
        var groups = new HashSet<string>(StringComparer.Ordinal);

        if (_broker is not null)
        {
            foreach (var subscription in _broker.Subscriptions.GetAll())
            {
                if (subscription.Filter.IsShared && subscription.Filter.Matches(topic))
                {
                    groups.Add(subscription.Filter.SharedGroup!);
                }
            }
        }

        foreach (var peer in peers)
        {
            foreach (var group in peer.SharedGroups(topic))
            {
                groups.Add(group);
            }
        }

        return groups.OrderBy(group => group, StringComparer.Ordinal).ToList();
    }

    /// <summary>Live nodes that host the group for this topic, self included.</summary>
    /// <remarks>先按 nodeId 排序再取模，保证所有节点算出同一个顺序。</remarks>
    /// <param name="topic">Topic being published.</param>
    /// <param name="group">Shared group name.</param>
    /// <param name="peers">Alive peers.</param>
    private List<string> SharedCandidates(string topic, string group, List<Member> peers)
    {
        var candidates = new List<string>();

        if (_broker is not null
            && _self is not null
            && _broker.Subscriptions.GetAll().Any(subscription =>
                subscription.Filter.IsShared
                && string.Equals(subscription.Filter.SharedGroup, group, StringComparison.Ordinal)
                && subscription.Filter.Matches(topic)))
        {
            candidates.Add(_self.NodeId);
        }

        foreach (var peer in peers)
        {
            if (peer.HasShared(topic, group))
            {
                candidates.Add(peer.NodeId);
            }
        }

        candidates.Sort(StringComparer.Ordinal);
        return candidates;
    }

    /// <summary>Deterministic index so every node picks the same owner.</summary>
    /// <remarks>FNV-1a，输入是 messageId 与组名。</remarks>
    /// <param name="messageId">Stable message id.</param>
    /// <param name="group">Shared group name.</param>
    /// <param name="count">Candidate count.</param>
    private static int StableIndex(string messageId, string group, int count)
    {
        var hash = 2166136261u;

        foreach (var b in System.Text.Encoding.UTF8.GetBytes(messageId + "\0" + group))
        {
            hash = (hash ^ b) * 16777619u;
        }

        return (int)(hash % (uint)count);
    }

    /// <summary>True when this node should deliver the shared group.</summary>
    /// <remarks>只有被选中的节点投递，其余节点跳过，避免重复。</remarks>
    /// <param name="topic">Topic being published.</param>
    /// <param name="group">Shared group name.</param>
    /// <param name="messageId">Stable message id.</param>
    public bool OwnsSharedGroup(string topic, string group, string messageId)
    {
        if (!Enabled || _self is null)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(_options.NodeTimeoutSeconds);
        var peers = _members.Values
            .Where(member => member.NodeId != _self.NodeId && member.Status(now, timeout) == "alive")
            .ToList();

        var candidates = SharedCandidates(topic, group, peers);

        if (candidates.Count == 0)
        {
            return true;
        }

        return string.Equals(
            candidates[StableIndex(messageId, group, candidates.Count)],
            _self.NodeId,
            StringComparison.Ordinal);
    }

    /// <summary>Removes a node that announced its departure.</summary>
    /// <remarks>主动离开立即摘除，不必等超时。</remarks>
    /// <param name="nodeId">Node identifier.</param>
    public void Leave(string nodeId)
    {
        if (_self is not null && nodeId != _self.NodeId)
        {
            _members.TryRemove(nodeId, out _);
            _logger.LogInformation("Cluster node {NodeId} left the cluster", nodeId);
        }
    }

    /// <summary>Messages forwarded to peers since start.</summary>
    /// <remarks>只统计本节点发出去的。</remarks>
    public long PublishesSent => Interlocked.Read(ref _publishesSent);

    /// <summary>Messages accepted from peers since start.</summary>
    /// <remarks>包含普通消息与共享专用消息。</remarks>
    public long PublishesReceived => Interlocked.Read(ref _publishesReceived);

    private async Task HeartbeatLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatSeconds)));

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await BroadcastAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // A single unreachable or unresolvable peer must not stop the
                    // heartbeat loop; retry on the next tick.
                    _logger.LogWarning(ex, "Cluster heartbeat round failed; retrying on the next tick");
                }

                if (!await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
    }

    private async Task BroadcastAsync(CancellationToken cancellationToken)
    {
        if (_self is null)
        {
            return;
        }

        var filters = LocalFilters();
        _members[_self.NodeId] = _self with { RawFilters = filters, LastSeen = DateTimeOffset.UtcNow };

        var urls = _members.Values
            .Where(member => member.NodeId != _self.NodeId)
            .Select(member => member.AdvertiseUrl)
            .Concat(_options.Seeds)
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.TrimEnd('/'))
            .Where(url => !string.Equals(url, _self.AdvertiseUrl, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (urls.Count == 0)
        {
            return;
        }

        await Parallel.ForEachAsync(
            urls,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 8,
                CancellationToken = cancellationToken,
            },
            async (url, token) => await SendHeartbeatAsync(url, filters, token).ConfigureAwait(false))
            .ConfigureAwait(false);

        PruneDeadMembers(DateTimeOffset.UtcNow);
        PruneSeenMessages(DateTimeOffset.UtcNow);
    }

    private async Task SendHeartbeatAsync(string baseUrl, string[] filters, CancellationToken cancellationToken)
    {
        if (_self is null)
        {
            return;
        }

        var request = new ClusterHeartbeatRequest(
            _self.NodeId,
            _self.AdvertiseUrl,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            filters);

        var response = await PostHeartbeatAsync(baseUrl, request, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;

        foreach (var member in response.Members)
        {
            MergeMember(member.NodeId, member.AdvertiseUrl, member.Filters, now);
        }
    }

    private async Task<ClusterHeartbeatResponse?> PostHeartbeatAsync(
        string baseUrl,
        ClusterHeartbeatRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await PostAsync<ClusterHeartbeatRequest, ClusterHeartbeatResponse>(
                baseUrl,
                "/internal/cluster/heartbeat",
                request,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Peers can be unresolvable while their container or DNS entry is
            // still starting; treat the tick as a missed heartbeat.
            _logger.LogDebug(ex, "Heartbeat to {Url} failed", baseUrl);
            return null;
        }
    }

    private async Task SendPublishAsync(Member member, ClusterPublishMessage message, CancellationToken cancellationToken)
    {
        try
        {
            await PostAsync<ClusterPublishMessage, object>(
                member.AdvertiseUrl,
                "/internal/cluster/publish",
                message,
                cancellationToken).ConfigureAwait(false);

            Interlocked.Increment(ref _publishesSent);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to forward a publish to node {NodeId}", member.NodeId);
        }
    }

    private async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string baseUrl,
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request, ClusterJson.Options);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var signature = ClusterSignature.Sign(_options.Secret, timestamp, json);

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri($"{baseUrl.TrimEnd('/')}{path}"));

        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");
        httpRequest.Headers.TryAddWithoutValidation(ClusterSignature.TimestampHeader, timestamp);
        httpRequest.Headers.TryAddWithoutValidation(ClusterSignature.SignatureHeader, signature);

        using var response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                _logger.LogDebug(
                    "Cluster request to {Url} returned {StatusCode}",
                    httpRequest.RequestUri,
                    (int)response.StatusCode);
            }

            return default;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        return string.IsNullOrEmpty(body)
            ? default
            : JsonSerializer.Deserialize<TResponse>(body, ClusterJson.Options);
    }

    private string[] LocalFilters()
    {
        if (_broker is null)
        {
            return [];
        }

        return _broker.Subscriptions.GetAll()
            .Select(subscription => subscription.Filter.Original)
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(1, _options.MaxFiltersPerHeartbeat))
            .ToArray();
    }

    private void MergeMember(string nodeId, string advertiseUrl, string[] filters, DateTimeOffset now)
    {
        if (_self is null
            || string.IsNullOrWhiteSpace(nodeId)
            || nodeId.Length > 128
            || nodeId == _self.NodeId
            || string.IsNullOrWhiteSpace(advertiseUrl)
            || !Uri.TryCreate(advertiseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        var normalizedFilters = filters
            .Where(filter => !string.IsNullOrEmpty(filter))
            .Distinct(StringComparer.Ordinal)
            .Take(Math.Max(1, _options.MaxFiltersPerHeartbeat))
            .ToArray();

        if (!_members.TryGetValue(nodeId, out var previous))
        {
            _logger.LogInformation("Cluster node {NodeId} joined at {AdvertiseUrl}", nodeId, advertiseUrl);
        }
        else if (!string.Equals(previous.AdvertiseUrl, advertiseUrl, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Cluster node {NodeId} changed address from {OldAddress} to {NewAddress}",
                nodeId,
                previous.AdvertiseUrl,
                advertiseUrl);
        }

        _members[nodeId] = new Member(nodeId, advertiseUrl.TrimEnd('/'), now, normalizedFilters);
    }

    private void PruneDeadMembers(DateTimeOffset now)
    {
        if (_self is null)
        {
            return;
        }

        var cutoff = now - TimeSpan.FromSeconds(_options.NodeTimeoutSeconds * 3.0);

        foreach (var (nodeId, member) in _members)
        {
            if (nodeId != _self.NodeId && member.LastSeen < cutoff)
            {
                _members.TryRemove(nodeId, out _);
                _logger.LogWarning("Cluster node {NodeId} timed out and was removed", nodeId);
            }
        }
    }

    private void PruneSeenMessages(DateTimeOffset now)
    {
        if (_seenMessages.Count < 10_000)
        {
            return;
        }

        var cutoff = now.AddMinutes(-10);

        foreach (var (messageId, seenAt) in _seenMessages)
        {
            if (seenAt < cutoff)
            {
                _seenMessages.TryRemove(messageId, out _);
            }
        }
    }

    private static ClusterPropertyDto EncodeProperty(MqttProperty property)
    {
        return MqttProperties.GetDataType(property.Id) switch
        {
            MqttPropertyDataType.Byte => new ClusterPropertyDto((int)property.Id, null, (byte)property.Value, null, null),
            MqttPropertyDataType.UInt16 => new ClusterPropertyDto((int)property.Id, null, (ushort)property.Value, null, null),
            MqttPropertyDataType.UInt32 => new ClusterPropertyDto((int)property.Id, null, (uint)property.Value, null, null),
            MqttPropertyDataType.VariableByteInteger => new ClusterPropertyDto((int)property.Id, null, (int)property.Value, null, null),
            MqttPropertyDataType.String => new ClusterPropertyDto((int)property.Id, (string)property.Value, null, null, null),
            MqttPropertyDataType.Binary => new ClusterPropertyDto(
                (int)property.Id,
                null,
                null,
                Convert.ToBase64String((byte[])property.Value),
                null),
            MqttPropertyDataType.StringPair => new ClusterPropertyDto(
                (int)property.Id,
                ((KeyValuePair<string, string>)property.Value).Key,
                null,
                null,
                ((KeyValuePair<string, string>)property.Value).Value),
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };
    }

    private static object DecodeProperty(ClusterPropertyDto property)
    {
        return MqttProperties.GetDataType((MqttPropertyId)property.Id) switch
        {
            MqttPropertyDataType.Byte => (byte)(property.Number ?? 0),
            MqttPropertyDataType.UInt16 => (ushort)(property.Number ?? 0),
            MqttPropertyDataType.UInt32 => (uint)(property.Number ?? 0),
            MqttPropertyDataType.VariableByteInteger => (int)(property.Number ?? 0),
            MqttPropertyDataType.String => property.Text ?? string.Empty,
            MqttPropertyDataType.Binary => Convert.FromBase64String(property.Binary ?? string.Empty),
            MqttPropertyDataType.StringPair => new KeyValuePair<string, string>(
                property.Text ?? string.Empty,
                property.UserValue ?? string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(property)),
        };
    }

    /// <summary>Membership row exchanged in heartbeats and returned by the API.</summary>
    /// <remarks>JSON 契约，改字段名会影响滚动升级的兼容性。</remarks>
    /// <param name="NodeId">Member identifier.</param>
    /// <param name="AdvertiseUrl">Base URL for heartbeats and forwarding.</param>
    /// <param name="Status">alive, suspect or dead.</param>
    /// <param name="LastSeenUnix">Last successful contact, Unix seconds.</param>
    /// <param name="Filters">Filters the member advertised.</param>
    public sealed record MemberSnapshot(
        string NodeId,
        string AdvertiseUrl,
        string Status,
        long LastSeenUnix,
        string[] Filters);

    private sealed record Member(
        string NodeId,
        string AdvertiseUrl,
        DateTimeOffset LastSeen,
        string[] RawFilters)
    {
        private TopicFilter[]? _parsed;

        /// <summary>Parsed filters, computed once per member.</summary>
    /// <remarks>每个成员只解析一次，心跳很频繁。</remarks>
        private TopicFilter[] Parsed => _parsed ??= RawFilters
            .Select(filter => TopicFilter.TryParse(filter, out var parsed, out _) ? parsed : null)
            .Where(filter => filter is not null)
            .ToArray()!;

        /// <summary>True when a non-shared filter matches the topic.</summary>
    /// <remarks>共享过滤器不参与普通扇出，否则同一组被多个节点投递。</remarks>
        /// <param name="topic">Concrete topic name.</param>
        public bool MatchesNormal(string topic)
            => Parsed.Any(filter => !filter.IsShared && filter.Matches(topic));

        /// <summary>Shared groups this member hosts for the topic.</summary>
    /// <remarks>源节点据此计算候选节点。</remarks>
        /// <param name="topic">Concrete topic name.</param>
        public IEnumerable<string> SharedGroups(string topic)
            => Parsed
                .Where(filter => filter.IsShared && filter.Matches(topic))
                .Select(filter => filter.SharedGroup!)
                .Distinct(StringComparer.Ordinal);

        /// <summary>True when this node hosts the shared group for the topic.</summary>
    /// <remarks>有一个匹配的共享过滤器即可。</remarks>
        /// <param name="topic">Concrete topic name.</param>
        /// <param name="group">Shared group name.</param>
        public bool HasShared(string topic, string group)
            => Parsed.Any(filter => filter.IsShared
                && string.Equals(filter.SharedGroup, group, StringComparison.Ordinal)
                && filter.Matches(topic));

        public string Status(DateTimeOffset now, TimeSpan timeout)
        {
            var age = now - LastSeen;

            if (age <= timeout)
            {
                return "alive";
            }

            return age <= timeout * 2 ? "suspect" : "dead";
        }
    }
}
