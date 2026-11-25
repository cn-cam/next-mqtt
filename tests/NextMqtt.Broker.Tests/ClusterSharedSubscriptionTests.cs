using NextMqtt.Broker.Clustering;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Subscriptions;
using NextMqtt.Core.Protocol;
using NextMqtt.Core.Topics;

namespace NextMqtt.Broker.Tests;

public sealed class ClusterSharedSubscriptionTests
{
    private const string Secret = "cluster-shared-secret-1234567890";

    private static ClusterRouter CreateRouter(string nodeId, string peerId, ClusterHttpNetwork network, HttpClient http)
    {
        var options = new ClusterOptions
        {
            Enabled = true,
            NodeId = nodeId,
            AdvertiseUrl = $"http://{nodeId}",
            Secret = Secret,
            Seeds = [$"http://{peerId}"],
            HeartbeatSeconds = 1,
            NodeTimeoutSeconds = 10,
        };

        return new ClusterRouter(
            options,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ClusterRouter>.Instance,
            http);
    }

    private static Subscription Shared(string clientId, string group, string filter)
        => new()
        {
            ClientId = clientId,
            Filter = TopicFilter.Parse($"$share/{group}/{filter}"),
            Options = new MqttSubscriptionOptions(MqttQoS.AtMostOnce),
        };

    [Fact]
    public async Task Shared_group_delivers_exactly_once_across_nodes()
    {
        var network = new ClusterHttpNetwork();
        using var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(5) };
        var routerA = CreateRouter("node-a", "node-b", network, http);
        var routerB = CreateRouter("node-b", "node-a", network, http);
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
            var (_, _, local) = await TestBroker.ConnectAsync("local-member", broker: brokerA);
            var (_, _, remote1) = await TestBroker.ConnectAsync("remote-member-1", broker: brokerB);
            var (_, _, remote2) = await TestBroker.ConnectAsync("remote-member-2", broker: brokerB);

            brokerA.Subscriptions.AddOrReplace(Shared("local-member", "workers", "jobs/#"));
            brokerB.Subscriptions.AddOrReplace(Shared("remote-member-1", "workers", "jobs/#"));
            brokerB.Subscriptions.AddOrReplace(Shared("remote-member-2", "workers", "jobs/#"));

            // Let the shared filters propagate in a heartbeat.
            await Task.Delay(2500);

            await brokerA.PublishAsync("jobs/1", "work"u8.ToArray(), MqttQoS.AtMostOnce);

            await Task.Delay(1500);

            var delivered = local.Publishes().Count + remote1.Publishes().Count + remote2.Publishes().Count;
            Assert.Equal(1, delivered);
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
    public async Task Shared_subscription_on_a_peer_receives_local_publishes()
    {
        var network = new ClusterHttpNetwork();
        using var http = new HttpClient(network) { Timeout = TimeSpan.FromSeconds(5) };
        var routerA = CreateRouter("node-a", "node-b", network, http);
        var routerB = CreateRouter("node-b", "node-a", network, http);
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
            var (_, _, remote) = await TestBroker.ConnectAsync("peer-member", broker: brokerB);
            brokerB.Subscriptions.AddOrReplace(Shared("peer-member", "workers", "jobs/#"));

            await Task.Delay(2500);

            await brokerA.PublishAsync("jobs/2", "work"u8.ToArray(), MqttQoS.AtMostOnce);

            await Task.Delay(1500);

            var message = Assert.Single(remote.Publishes());
            Assert.Equal("jobs/2", message.Topic);
        }
        finally
        {
            await routerA.StopAsync();
            await routerB.StopAsync();
            await brokerA.DisposeAsync();
            await brokerB.DisposeAsync();
        }
    }
}
