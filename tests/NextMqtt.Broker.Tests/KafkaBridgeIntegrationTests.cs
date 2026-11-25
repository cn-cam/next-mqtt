using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker.Integrations;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

/// <summary>
/// Smoke test against a real Kafka cluster. It only runs when
/// <c>NEXTMQTT_KAFKA_BOOTSTRAP</c> points at one; otherwise it is a no-op so the
/// default test run stays offline and deterministic.
/// </summary>
public sealed class KafkaBridgeIntegrationTests
{
    /// <summary>Publishes one message and waits for Kafka to acknowledge it.</summary>
    [Fact]
    public async Task Forwards_to_a_real_cluster_when_configured()
    {
        var bootstrap = Environment.GetEnvironmentVariable("NEXTMQTT_KAFKA_BOOTSTRAP");

        if (string.IsNullOrWhiteSpace(bootstrap))
        {
            // No cluster configured: nothing to verify, and no failure either.
            return;
        }

        await using var bridge = new KafkaBridge(
            new KafkaOptions
            {
                Enabled = true,
                BootstrapServers = bootstrap,
                TopicPrefix = "nextmqtt-test.",
                Filter = "kafka/#",
                ClientId = "nextmqtt-tests",
                LingerMs = 10,
            },
            NullLogger<KafkaBridge>.Instance);

        bridge.Publish(new PublishPacket
        {
            Topic = "kafka/smoke",
            Payload = "ping"u8.ToArray(),
        });

        for (var attempt = 0; attempt < 200 && bridge.Forwarded == 0 && bridge.Failed == 0; attempt++)
        {
            await Task.Delay(50);
        }

        Assert.Equal(1, bridge.Forwarded);
    }
}
