using System.Net;
using System.Text;
using System.Text.Json;
using NextMqtt.Broker.Clustering;

namespace NextMqtt.Broker.Tests;

/// <summary>Routes cluster HTTP calls between in-process routers.</summary>
internal sealed class ClusterHttpNetwork : HttpMessageHandler
{
    private readonly Dictionary<string, ClusterRouter> _nodes = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string host, ClusterRouter router) => _nodes[host] = router;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var host = request.RequestUri?.Host ?? string.Empty;

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
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

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
