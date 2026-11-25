using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker.Integrations;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

/// <summary>
/// RabbitMQ smoke test. It only runs when the <c>NEXTMQTT_RABBIT_URL</c> variable
/// points at a broker, for example
/// <c>amqp://user:pass@127.0.0.1:5672/</c>; otherwise it returns immediately so
/// the default suite stays offline.
/// </summary>
public sealed class RabbitBridgeTests
{
    [Fact]
    public void Routing_keys_map_to_dotted_safe_names()
    {
        Assert.Equal("mqtt.sensors.room_1.temp", RabbitBridge.MapRoutingKey("mqtt.", "sensors/room 1/temp"));
        Assert.Equal("mqtt.a.b", RabbitBridge.MapRoutingKey("mqtt.", "a/b"));
        Assert.Equal("mqtt.x_y", RabbitBridge.MapRoutingKey("mqtt.", "x*y"));
        Assert.Equal("plain", RabbitBridge.MapRoutingKey(string.Empty, "plain"));
    }

    [Fact]
    public async Task Publishes_to_a_real_broker_when_configured()
    {
        var urlText = Environment.GetEnvironmentVariable("NEXTMQTT_RABBIT_URL");

        if (string.IsNullOrWhiteSpace(urlText) || !Uri.TryCreate(urlText, UriKind.Absolute, out var url))
        {
            // No broker configured: nothing to verify, and no failure either.
            return;
        }

        var userInfo = url.UserInfo.Split(':', 2);

        await using var bridge = new RabbitBridge(
            new RabbitOptions
            {
                Enabled = true,
                HostName = url.Host,
                Port = url.Port > 0 ? url.Port : 5672,
                UserName = Uri.UnescapeDataString(userInfo[0]),
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty,
                Exchange = "nextmqtt-tests",
                RoutingKeyPrefix = "test.",
                Filter = "rabbit/#",
            },
            NullLogger<RabbitBridge>.Instance);

        bridge.Publish(new PublishPacket
        {
            Topic = "rabbit/smoke",
            Payload = "ping"u8.ToArray(),
        });

        for (var attempt = 0; attempt < 100 && bridge.Forwarded == 0 && bridge.Failed == 0; attempt++)
        {
            await Task.Delay(100);
        }

        Assert.Equal(1, bridge.Forwarded);
    }
}
