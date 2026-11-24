using NextMqtt.Broker.Options;
using NextMqtt.Broker.RateLimiting;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Server.Tests;

public class RateLimitTests
{
    private static ConnectPacket Connect(string clientId)
        => new()
        {
            ClientId = clientId,
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            KeepAliveSeconds = 30,
            Properties = new MqttProperties(),
        };

    [Fact]
    public void Token_bucket_allows_a_burst_then_throttles()
    {
        var bucket = new TokenBucket(refillPerSecond: 10, capacity: 3);

        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());
    }

    [Fact]
    public async Task Token_bucket_refills_over_time()
    {
        var bucket = new TokenBucket(refillPerSecond: 50, capacity: 1);

        Assert.True(bucket.TryTake());
        Assert.False(bucket.TryTake());

        await Task.Delay(60);

        Assert.True(bucket.TryTake());
    }

    [Theory]
    [InlineData("127.0.0.1:50000", "127.0.0.1")]
    [InlineData("[::1]:50000", "::1")]
    [InlineData("::1", "::1")]
    [InlineData("", "unknown")]
    public void Addresses_are_normalized_without_ports(string input, string expected)
        => Assert.Equal(expected, ConnectionRateLimiter.Normalize(input));

    [Fact]
    public void Connection_limiter_buckets_per_address()
    {
        var limiter = new ConnectionRateLimiter(new RateLimitOptions
        {
            ConnectPerSecondPerIp = 1,
            ConnectBurst = 1,
        });

        Assert.True(limiter.TryAcquire("10.0.0.1:1000"));
        Assert.False(limiter.TryAcquire("10.0.0.1:2000"));
        Assert.True(limiter.TryAcquire("10.0.0.2:1000"));
    }

    [Fact]
    public async Task Connect_rate_limit_rejects_with_connection_rate_exceeded()
    {
        await using var server = MqttTestServer.Start(configure: options => options.RateLimits = new RateLimitOptions
        {
            ConnectPerSecondPerIp = 1,
            ConnectBurst = 1,
        });

        await using var first = await server.ConnectAsync(Connect("first"));
        Assert.Equal(MqttReasonCodes.Success, first.ConnAck.ReasonCode);

        await using var second = await server.ConnectAsync(Connect("second"));
        Assert.Equal(MqttReasonCodes.ConnectionRateExceeded, second.ConnAck.ReasonCode);
        Assert.Equal(1, server.Broker.Metrics.ConnectionsRateLimited);
    }

    [Fact]
    public async Task Publish_rate_limit_drops_messages_but_still_acknowledges()
    {
        await using var server = MqttTestServer.Start(configure: options => options.RateLimits = new RateLimitOptions
        {
            PublishPerSecondPerClient = 1,
            PublishBurst = 2,
        });

        await using var subscriber = await server.ConnectAsync(Connect("sub"));
        await using var publisher = await server.ConnectAsync(Connect("pub"));

        await subscriber.SubscribeAsync("quota/#", MqttQoS.AtMostOnce);

        for (var index = 0; index < 5; index++)
        {
            var packetId = await publisher.PublishAsync("quota/x", $"m{index}", MqttQoS.AtLeastOnce);
            var pubAck = await publisher.ReadAsync<PubAckPacket>();
            Assert.Equal(packetId, pubAck.PacketId);
            Assert.Equal(MqttReasonCodes.Success, pubAck.ReasonCode);
        }

        // The burst admits two messages; the remaining three are dropped but
        // still acknowledged, so the publisher never retransmits.
        Assert.Equal("quota/x", (await subscriber.ReadAsync<PublishPacket>()).Topic);
        Assert.Equal("quota/x", (await subscriber.ReadAsync<PublishPacket>()).Topic);

        using var quiet = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => subscriber.ReadAsync<PublishPacket>(quiet.Token));

        Assert.Equal(3, server.Broker.Metrics.PublishesRateLimited);
        Assert.Equal(3, server.Broker.Metrics.DroppedMessages);
    }
}
