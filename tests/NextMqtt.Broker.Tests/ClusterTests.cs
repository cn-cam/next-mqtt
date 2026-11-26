using System.Net;
using System.Text;
using System.Text.Json;
using NextMqtt.Broker.Clustering;
using NextMqtt.Broker.Diagnostics;
using NextMqtt.Broker.Storage;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

/// <summary>Routes cluster HTTP calls between in-process routers.</summary>
internal sealed class ClusterHttpNetwork : HttpMessageHandler
{
    private readonly Dictionary<string, ClusterRouter> _nodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, MqttBroker> _brokers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hosts that should fail to resolve, simulating startup DNS gaps.</summary>
    public HashSet<string> UnresolvableHosts { get; } = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string host, ClusterRouter router) => _nodes[host] = router;

    /// <summary>Registers the broker that answers session claims for a host.</summary>
    public void RegisterBroker(string host, MqttBroker broker) => _brokers[host] = broker;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? string.Empty;

        if (UnresolvableHosts.Contains(host))
        {
            throw new HttpRequestException($"Name or service not known ({host})");
        }

        if (!_nodes.TryGetValue(host, out var router))
        {
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        var body = await request.Content!.ReadAsStringAsync(cancellationToken);
        var timestamp = request.Headers.TryGetValues(ClusterSignature.TimestampHeader, out var timestamps)
            ? timestamps.First()
            : string.Empty;
        var signature = request.Headers.TryGetValues(ClusterSignature.SignatureHeader, out var signatures)
            ? signatures.First()
            : string.Empty;

        if (!ClusterSignature.Verify(router.Secret, timestamp, signature, body, out _))
        {
            return new HttpResponseMessage(HttpStatusCode.Unauthorized);
        }

        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/heartbeat", StringComparison.Ordinal))
        {
            var heartbeat = JsonSerializer.Deserialize<ClusterHeartbeatRequest>(body, ClusterJson.Options)!;
            var response = await router.ProcessHeartbeatAsync(heartbeat, cancellationToken);
            var json = JsonSerializer.SerializeToUtf8Bytes(response, ClusterJson.Options);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(json),
            };
        }

        if (path.EndsWith("/publish", StringComparison.Ordinal))
        {
            var message = JsonSerializer.Deserialize<ClusterPublishMessage>(body, ClusterJson.Options)!;
            var accepted = await router.ProcessPublishAsync(message, cancellationToken);
            return new HttpResponseMessage(accepted ? HttpStatusCode.Accepted : HttpStatusCode.Conflict);
        }

        if (path.EndsWith("/session/claim", StringComparison.Ordinal))
        {
            var claim = JsonSerializer.Deserialize<ClusterSessionClaim>(body, ClusterJson.Options)!;

            if (!_brokers.TryGetValue(host, out var broker))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var transfer = await broker.DetachSessionAsync(claim.ClientId, cancellationToken);

            if (transfer is null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(transfer, ClusterJson.Options);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            };
        }

        if (path.EndsWith("/session/replicate", StringComparison.Ordinal))
        {
            var replica = JsonSerializer.Deserialize<ClusterSessionReplica>(body, ClusterJson.Options)!;

            if (!_brokers.TryGetValue(host, out var broker))
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            broker.StoreSessionReplica(replica);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        if (path.EndsWith("/session/replica", StringComparison.Ordinal))
        {
            var claim = JsonSerializer.Deserialize<ClusterSessionClaim>(body, ClusterJson.Options)!;

            if (!_brokers.TryGetValue(host, out var broker) || broker.GetSessionReplica(claim.ClientId) is not { } replica)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            var payload = JsonSerializer.SerializeToUtf8Bytes(replica, ClusterJson.Options);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            };
        }

        return new HttpResponseMessage(HttpStatusCode.NotFound);
    }
}

public class ClusterTests
{
    [Fact]
    public void Signature_roundtrips_and_rejects_tampering()
    {
        const string secret = "a-shared-secret-with-entropy";
        const string body = """{"nodeId":"a"}""";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);

        var signature = ClusterSignature.Sign(secret, timestamp, body);

        Assert.True(ClusterSignature.Verify(secret, timestamp, signature, body, out _));
        Assert.False(ClusterSignature.Verify(secret, timestamp, signature, body + " ", out _));
        Assert.False(ClusterSignature.Verify(secret, "0", signature, body, out _));
        Assert.False(ClusterSignature.Verify(secret, timestamp, "zz", body, out _));
    }

    [Fact]
    public async Task Two_nodes_share_membership_and_route_publishes()
    {
        const string secret = "cluster-test-secret-1234567890";

        var network = new ClusterHttpNetwork();

        var optionsA = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-a",
            AdvertiseUrl = "http://node-a",
            Secret = secret,
            Seeds = ["http://node-b"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
        };

        var optionsB = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-b",
            AdvertiseUrl = "http://node-b",
            Secret = secret,
            Seeds = ["http://node-a"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
        };

        var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(5) };

        var routerA = new ClusterRouter(optionsA, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);
        var routerB = new ClusterRouter(optionsB, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);

        network.Register("node-a", routerA);
        network.Register("node-b", routerB);

        var brokerA = TestBroker.Create(cluster: routerA);
        var brokerB = TestBroker.Create(cluster: routerB);

        routerA.AttachBroker(brokerA);
        routerB.AttachBroker(brokerB);

        await routerA.StartAsync();
        await routerB.StartAsync();

        try
        {
            var (_, _, subscriptionConnection) = await TestBroker.ConnectAsync("remote-sub", broker: brokerB);
            brokerB.Subscriptions.AddOrReplace(new NextMqtt.Broker.Subscriptions.Subscription
            {
                ClientId = "remote-sub",
                Filter = NextMqtt.Core.Topics.TopicFilter.Parse("cluster/#"),
                Options = new NextMqtt.Core.Protocol.MqttSubscriptionOptions(NextMqtt.Core.Protocol.MqttQoS.AtLeastOnce),
            });

            var deadline = DateTimeOffset.UtcNow.AddSeconds(8);

            while (DateTimeOffset.UtcNow < deadline
                   && (!routerA.GetSnapshot().Nodes.Any(node => node.NodeId == "node-b" && node.Status == "alive")
                       || !routerB.GetSnapshot().Nodes.Any(node => node.NodeId == "node-a")))
            {
                await Task.Delay(100);
            }

            Assert.Contains(routerA.GetSnapshot().Nodes, node => node.NodeId == "node-b" && node.Status == "alive");
            Assert.Contains(routerB.GetSnapshot().Nodes, node => node.NodeId == "node-a");

            // Allow the subscription filter to propagate in the next heartbeat.
            await Task.Delay(1500);

            await brokerA.PublishAsync("cluster/hello", "world"u8.ToArray(), NextMqtt.Core.Protocol.MqttQoS.AtLeastOnce);

            deadline = DateTimeOffset.UtcNow.AddSeconds(5);

            while (DateTimeOffset.UtcNow < deadline && subscriptionConnection.Publishes().Count == 0)
            {
                await Task.Delay(50);
            }

            var publish = Assert.Single(subscriptionConnection.Publishes());
            Assert.Equal("cluster/hello", publish.Topic);
            Assert.Equal("world"u8.ToArray(), publish.Payload);
            Assert.True(routerA.PublishesSent >= 1);
            Assert.True(routerB.PublishesReceived >= 1);
        }
        finally
        {
            await routerA.StopAsync();
            await routerB.StopAsync();
            http.Dispose();
        }
    }

    /// <summary>Returns one canned heartbeat response for every request.</summary>
    private sealed class CannedHeartbeatHandler : HttpMessageHandler
    {
        private readonly ClusterHeartbeatResponse _response;

        public CannedHeartbeatHandler(ClusterHeartbeatResponse response) => _response = response;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(_response, ClusterJson.Options);

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(json),
            });
        }
    }

    [Fact]
    public async Task Relayed_member_does_not_refresh_a_dead_node()
    {
        const string secret = "cluster-test-secret-1234567890";

        // A peer keeps reporting a node that has been silent for a long time.
        var response = new ClusterHeartbeatResponse(
            "node-b",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            [new ClusterMemberDto("ghost", "http://ghost", "alive", 1, [])]);

        var options = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-a",
            AdvertiseUrl = "http://node-a",
            Secret = secret,
            Seeds = ["http://node-b"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 2,
        };

        var router = new ClusterRouter(
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance,
            new HttpClient(new CannedHeartbeatHandler(response)));

        var broker = TestBroker.Create(cluster: router);
        router.AttachBroker(broker);

        await router.StartAsync();

        try
        {
            // Poll instead of sleeping a fixed time: a cold CI runner can take a
            // few seconds to finish the first heartbeat round.
            var deadline = DateTimeOffset.UtcNow.AddSeconds(10);

            while (DateTimeOffset.UtcNow < deadline
                && router.GetSnapshot().Nodes.Any(node => node.NodeId == "ghost"))
            {
                await Task.Delay(200);
            }

            Assert.DoesNotContain(router.GetSnapshot().Nodes, node => node.NodeId == "ghost");
        }
        finally
        {
            await router.StopAsync();
            await broker.DisposeAsync();
        }
    }

    [Fact]
    public async Task Session_moves_to_another_node_on_reconnect()
    {
        const string secret = "cluster-test-secret-1234567890";
        var network = new ClusterHttpNetwork();

        var optionsA = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-a",
            AdvertiseUrl = "http://node-a",
            Secret = secret,
            Seeds = ["http://node-b"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
        };

        var optionsB = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-b",
            AdvertiseUrl = "http://node-b",
            Secret = secret,
            Seeds = ["http://node-a"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
        };

        var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(5) };
        var routerA = new ClusterRouter(optionsA, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);
        var routerB = new ClusterRouter(optionsB, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);

        network.Register("node-a", routerA);
        network.Register("node-b", routerB);

        var brokerA = TestBroker.Create(cluster: routerA);
        var brokerB = TestBroker.Create(cluster: routerB);

        network.RegisterBroker("node-a", brokerA);
        network.RegisterBroker("node-b", brokerB);

        routerA.AttachBroker(brokerA);
        routerB.AttachBroker(brokerB);

        await routerA.StartAsync();
        await routerB.StartAsync();

        try
        {
            // Seed membership directly so the test does not depend on heartbeat
            // timing on a slow runner.
            await routerB.ProcessHeartbeatAsync(new ClusterHeartbeatRequest(
                "node-a",
                "http://node-a",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                []));

            var (_, sessionA, connectionA) = await TestBroker.ConnectAsync(
                "roamer",
                cleanStart: false,
                sessionExpiry: 300,
                broker: brokerA);

            brokerA.Subscriptions.AddOrReplace(new NextMqtt.Broker.Subscriptions.Subscription
            {
                ClientId = "roamer",
                Filter = NextMqtt.Core.Topics.TopicFilter.Parse("migrate/#"),
                Options = new MqttSubscriptionOptions(MqttQoS.AtLeastOnce),
            });

            var oldSession = sessionA;

            // Reconnect on the other node: it must claim the session instead of
            // starting from scratch.
            var attach = await brokerB.AttachSessionAsync(
                TestBroker.Connect("roamer", cleanStart: false, sessionExpiry: 300),
                NextMqtt.Broker.Security.ClientPrincipal.Anonymous("roamer"),
                new FakeConnection { ProtocolVersion = MqttProtocolVersion.V500 });

            Assert.True(attach.SessionPresent);
            Assert.Null(brokerA.Sessions.Get("roamer"));
            Assert.Contains(
                brokerB.Subscriptions.GetByClient("roamer"),
                subscription => subscription.Filter.Original == "migrate/#");
            Assert.True(connectionA.Disconnected);
            Assert.Equal(MqttReasonCodes.SessionTakenOver, connectionA.DisconnectReason);
            Assert.NotNull(oldSession);
        }
        finally
        {
            await routerA.StopAsync();
            await routerB.StopAsync();
            await brokerA.DisposeAsync();
            await brokerB.DisposeAsync();
        }
    }

    [Fact]
    public async Task Claimed_session_keeps_its_queued_messages()
    {
        const string secret = "cluster-test-secret-1234567890";
        var network = new ClusterHttpNetwork();

        var optionsA = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-a",
            AdvertiseUrl = "http://node-a",
            Secret = secret,
            Seeds = ["http://node-b"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
        };

        var optionsB = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-b",
            AdvertiseUrl = "http://node-b",
            Secret = secret,
            Seeds = ["http://node-a"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
        };

        var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(5) };
        var routerA = new ClusterRouter(optionsA, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);
        var routerB = new ClusterRouter(optionsB, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);

        network.Register("node-a", routerA);
        network.Register("node-b", routerB);

        var brokerA = TestBroker.Create(cluster: routerA);
        var brokerB = TestBroker.Create(cluster: routerB);

        network.RegisterBroker("node-a", brokerA);
        network.RegisterBroker("node-b", brokerB);

        routerA.AttachBroker(brokerA);
        routerB.AttachBroker(brokerB);

        await routerA.StartAsync();
        await routerB.StartAsync();

        try
        {
            await routerB.ProcessHeartbeatAsync(new ClusterHeartbeatRequest(
                "node-a",
                "http://node-a",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                []));

            var (_, session, connection) = await TestBroker.ConnectAsync(
                "queued",
                cleanStart: false,
                sessionExpiry: 300,
                broker: brokerA);

            // Detach the connection so deliveries land in the offline queue.
            session.Detach(connection);
            session.Deliver(new NextMqtt.Broker.Sessions.OutboundMessage
            {
                Topic = "migrate/queued",
                Payload = "hello"u8.ToArray(),
                QoS = MqttQoS.AtLeastOnce,
            });

            var newConnection = new FakeConnection { ProtocolVersion = MqttProtocolVersion.V500 };
            var attach = await brokerB.AttachSessionAsync(
                TestBroker.Connect("queued", cleanStart: false, sessionExpiry: 300),
                NextMqtt.Broker.Security.ClientPrincipal.Anonymous("queued"),
                newConnection);

            Assert.True(attach.SessionPresent);

            // Resume flushes the transferred queue to the new connection.
            var publish = Assert.Single(newConnection.Publishes());
            Assert.Equal("migrate/queued", publish.Topic);
            Assert.Equal("hello"u8.ToArray(), publish.Payload);
        }
        finally
        {
            await routerA.StopAsync();
            await routerB.StopAsync();
            await brokerA.DisposeAsync();
            await brokerB.DisposeAsync();
        }
    }

    [Fact]
    public async Task Session_is_recovered_from_a_replica_when_the_owner_is_gone()
    {
        const string secret = "cluster-test-secret-1234567890";
        var network = new ClusterHttpNetwork();

        var optionsA = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-a",
            AdvertiseUrl = "http://node-a",
            Secret = secret,
            Seeds = ["http://node-b"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
            SessionReplicaSeconds = 1,
        };

        var optionsB = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-b",
            AdvertiseUrl = "http://node-b",
            Secret = secret,
            Seeds = ["http://node-a"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
            SessionReplicaSeconds = 1,
        };

        var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(5) };
        var routerA = new ClusterRouter(optionsA, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);
        var routerB = new ClusterRouter(optionsB, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);

        network.Register("node-a", routerA);
        network.Register("node-b", routerB);

        var brokerA = TestBroker.Create(cluster: routerA);
        var brokerB = TestBroker.Create(cluster: routerB);

        network.RegisterBroker("node-a", brokerA);
        network.RegisterBroker("node-b", brokerB);

        routerA.AttachBroker(brokerA);
        routerB.AttachBroker(brokerB);

        await routerA.StartAsync();
        await routerB.StartAsync();
        await brokerA.StartAsync(CancellationToken.None);
        await brokerB.StartAsync(CancellationToken.None);

        try
        {
            await routerB.ProcessHeartbeatAsync(new ClusterHeartbeatRequest(
                "node-a",
                "http://node-a",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                []));

            var (_, session, connection) = await TestBroker.ConnectAsync(
                "replicated",
                cleanStart: false,
                sessionExpiry: 300,
                broker: brokerA);

            brokerA.Subscriptions.AddOrReplace(new NextMqtt.Broker.Subscriptions.Subscription
            {
                ClientId = "replicated",
                Filter = NextMqtt.Core.Topics.TopicFilter.Parse("replica/#"),
                Options = new MqttSubscriptionOptions(MqttQoS.AtLeastOnce),
            });

            session.Detach(connection);
            session.Deliver(new NextMqtt.Broker.Sessions.OutboundMessage
            {
                Topic = "replica/queued",
                Payload = "replica"u8.ToArray(),
                QoS = MqttQoS.AtLeastOnce,
            });

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);

            while (DateTimeOffset.UtcNow < deadline && brokerB.SessionReplicaCount == 0)
            {
                await Task.Delay(200);
            }

            Assert.Equal(1, brokerB.SessionReplicaCount);

            // The owner disappears without handing the session over.
            await routerA.StopAsync();
            await brokerA.StopAsync();

            var newConnection = new FakeConnection { ProtocolVersion = MqttProtocolVersion.V500 };
            var attach = await brokerB.AttachSessionAsync(
                TestBroker.Connect("replicated", cleanStart: false, sessionExpiry: 300),
                NextMqtt.Broker.Security.ClientPrincipal.Anonymous("replicated"),
                newConnection);

            Assert.True(attach.SessionPresent);
            Assert.Contains(
                brokerB.Subscriptions.GetByClient("replicated"),
                subscription => subscription.Filter.Original == "replica/#");

            var publish = Assert.Single(newConnection.Publishes());
            Assert.Equal("replica/queued", publish.Topic);
        }
        finally
        {
            await brokerA.DisposeAsync();
            await brokerB.DisposeAsync();
            await routerB.StopAsync();
        }
    }

    [Fact]
    public async Task Replica_from_a_live_owner_is_not_used()
    {
        const string secret = "cluster-test-secret-1234567890";
        var network = new ClusterHttpNetwork();

        var optionsA = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-a",
            AdvertiseUrl = "http://node-a",
            Secret = secret,
            Seeds = ["http://node-b"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
            SessionReplicaSeconds = 1,
        };

        var optionsB = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-b",
            AdvertiseUrl = "http://node-b",
            Secret = secret,
            Seeds = ["http://node-a"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
            SessionReplicaSeconds = 1,
        };

        var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(5) };
        var routerA = new ClusterRouter(optionsA, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);
        var routerB = new ClusterRouter(optionsB, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);

        network.Register("node-a", routerA);
        network.Register("node-b", routerB);

        var brokerA = TestBroker.Create(cluster: routerA);
        var brokerB = TestBroker.Create(cluster: routerB);

        network.RegisterBroker("node-a", brokerA);
        network.RegisterBroker("node-b", brokerB);

        routerA.AttachBroker(brokerA);
        routerB.AttachBroker(brokerB);

        await routerA.StartAsync();
        await routerB.StartAsync();
        await brokerA.StartAsync(CancellationToken.None);
        await brokerB.StartAsync(CancellationToken.None);

        try
        {
            await routerB.ProcessHeartbeatAsync(new ClusterHeartbeatRequest(
                "node-a",
                "http://node-a",
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                []));

            var (_, session, connection) = await TestBroker.ConnectAsync(
                "stale",
                cleanStart: false,
                sessionExpiry: 300,
                broker: brokerA);

            session.Detach(connection);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(15);

            while (DateTimeOffset.UtcNow < deadline && brokerB.SessionReplicaCount == 0)
            {
                await Task.Delay(200);
            }

            // The owner is still alive but the session is gone: a replica must
            // not resurrect it.
            brokerA.Sessions.Remove("stale");
            brokerA.Subscriptions.RemoveAll("stale");

            var attach = await brokerB.AttachSessionAsync(
                TestBroker.Connect("stale", cleanStart: false, sessionExpiry: 300),
                NextMqtt.Broker.Security.ClientPrincipal.Anonymous("stale"),
                new FakeConnection { ProtocolVersion = MqttProtocolVersion.V500 });

            Assert.False(attach.SessionPresent);
        }
        finally
        {
            await brokerA.DisposeAsync();
            await brokerB.DisposeAsync();
            await routerA.StopAsync();
            await routerB.StopAsync();
        }
    }

    /// <summary>Cluster router stub that only serves replicas and liveness.</summary>
    private sealed class ReplicaStub : IClusterRouter
    {
        private readonly ClusterSessionReplica? _replica;
        private readonly bool _alive;

        public ReplicaStub(ClusterSessionReplica? replica, bool alive)
        {
            _replica = replica;
            _alive = alive;
        }

        public bool Enabled => true;

        public ClusterSnapshot GetSnapshot() => new("node-x", "clustered", []);

        public ValueTask RouteAsync(PublishPacket publish, string messageId, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public bool OwnsSharedGroup(string topic, string group, string messageId) => true;

        public Task<PersistedSession?> ClaimSessionAsync(string clientId, CancellationToken cancellationToken = default)
            => Task.FromResult<PersistedSession?>(null);

        public bool ReplicatesSessions => true;

        public TimeSpan ReplicaInterval => TimeSpan.FromSeconds(5);

        public Task ReplicateSessionAsync(ClusterSessionReplica replica, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<ClusterSessionReplica?> FindSessionReplicaAsync(string clientId, CancellationToken cancellationToken = default)
            => Task.FromResult(_replica);

        public bool IsNodeAlive(string nodeId) => _alive;

        public ValueTask StartAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask StopAsync() => ValueTask.CompletedTask;
    }

    private static ClusterSessionReplica Replica(string clientId, string owner, long updatedUnix)
        => new(
            new NextMqtt.Broker.Storage.PersistedSession { ClientId = clientId },
            owner,
            7,
            updatedUnix);

    [Fact]
    public async Task Restored_session_owned_by_a_live_peer_is_dropped()
    {
        var now = DateTimeOffset.UtcNow;
        var broker = TestBroker.Create(cluster: new ReplicaStub(Replica("ghost", "node-b", now.AddSeconds(5).ToUnixTimeSeconds()), alive: true));

        var (_, session, connection) = await TestBroker.ConnectAsync(
            "ghost",
            cleanStart: false,
            sessionExpiry: 300,
            broker: broker);

        session.Detach(connection);

        Assert.Equal(1, await broker.DropStaleRestoredSessionsAsync());
        Assert.Null(broker.Sessions.Get("ghost"));

        await broker.DisposeAsync();
    }

    [Fact]
    public async Task Restored_session_is_kept_when_the_owner_is_this_node()
    {
        var now = DateTimeOffset.UtcNow;
        var broker = TestBroker.Create(cluster: new ReplicaStub(Replica("ghost", "node-x", now.AddSeconds(5).ToUnixTimeSeconds()), alive: true));

        var (_, session, connection) = await TestBroker.ConnectAsync(
            "ghost",
            cleanStart: false,
            sessionExpiry: 300,
            broker: broker);

        session.Detach(connection);

        Assert.Equal(0, await broker.DropStaleRestoredSessionsAsync());
        Assert.NotNull(broker.Sessions.Get("ghost"));

        await broker.DisposeAsync();
    }

    [Fact]
    public async Task Unresolvable_seed_does_not_stop_heartbeats()
    {
        const string secret = "cluster-test-secret-1234567890";

        var network = new ClusterHttpNetwork();

        var optionsA = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-a",
            AdvertiseUrl = "http://node-a",
            Secret = secret,
            Seeds = ["http://node-b"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 3,
        };

        var optionsB = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-b",
            AdvertiseUrl = "http://node-b",
            Secret = secret,
            Seeds = ["http://node-a"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 3,
        };

        var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(5) };

        var routerA = new ClusterRouter(optionsA, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);
        var routerB = new ClusterRouter(optionsB, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);

        network.Register("node-a", routerA);
        network.Register("node-b", routerB);

        var brokerA = TestBroker.Create(cluster: routerA);
        var brokerB = TestBroker.Create(cluster: routerB);

        routerA.AttachBroker(brokerA);
        routerB.AttachBroker(brokerB);

        // The seed is unresolvable, as can happen while a peer container is
        // still starting. The heartbeat loop must keep running regardless.
        network.UnresolvableHosts.Add("node-b");

        await routerA.StartAsync();
        await routerB.StartAsync();

        try
        {
            // Longer than NodeTimeoutSeconds: a loop that died on the first
            // failure stops refreshing the self entry and turns it dead.
            await Task.Delay(TimeSpan.FromSeconds(5));

            Assert.Contains(
                routerA.GetSnapshot().Nodes,
                node => node.NodeId == "node-a" && node.Status == "alive");
        }
        finally
        {
            await routerA.StopAsync();
            await routerB.StopAsync();
            http.Dispose();
        }
    }

    [Fact]
    public async Task Duplicate_remote_message_is_rejected()
    {
        const string secret = "cluster-test-secret-1234567890";
        var options = new NextMqtt.Broker.Options.ClusterOptions
        {
            Enabled = true,
            NodeId = "node-b",
            AdvertiseUrl = "http://node-b",
            Secret = secret,
            Seeds = [],
            HeartbeatSeconds = 60,
        };

        var network = new ClusterHttpNetwork();
        var http = new HttpClient(network);
        var router = new ClusterRouter(options, Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance, http);
        network.Register("node-b", router);

        var broker = TestBroker.Create(cluster: router);
        router.AttachBroker(broker);
        await router.StartAsync();

        var message = new ClusterPublishMessage(
            "duplicate-id",
            "node-a",
            0,
            "a/b",
            Convert.ToBase64String("x"u8.ToArray()),
            0,
            false,
            false,
            []);

        Assert.True(await router.ProcessPublishAsync(message));
        Assert.False(await router.ProcessPublishAsync(message));

        await router.StopAsync();
        http.Dispose();
    }
}
