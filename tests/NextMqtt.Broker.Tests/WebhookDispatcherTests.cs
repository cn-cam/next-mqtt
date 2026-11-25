using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker.Integrations;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

public sealed class WebhookDispatcherTests
{
    /// <summary>Captures requests and returns a scripted status code per attempt.</summary>
    private sealed class CaptureHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _responses = new();

        public List<string> Bodies { get; } = [];

        public CaptureHandler(params HttpStatusCode[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(
                _responses.Count > 0 ? _responses.Dequeue() : HttpStatusCode.OK);
        }
    }

    private static PublishPacket Publish(string topic, string payload, bool retain = false)
        => new()
        {
            Topic = topic,
            Payload = System.Text.Encoding.UTF8.GetBytes(payload),
            QoS = MqttQoS.AtLeastOnce,
            Retain = retain,
        };

    private static WebhookDispatcher Create(CaptureHandler handler, string filter = "audit/#")
        => new(
            new IntegrationsOptions
            {
                Webhooks =
                [
                    new WebhookOptions
                    {
                        Name = "audit",
                        Filter = filter,
                        Url = "http://localhost:9000/hook",
                        TimeoutMs = 1000,
                    },
                ],
            },
            new HttpClient(handler),
            NullLogger<WebhookDispatcher>.Instance);

    private static async Task<bool> WaitForAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return false;
    }

    [Fact]
    public async Task Matching_publish_is_posted_with_payload()
    {
        var handler = new CaptureHandler();
        await using var dispatcher = Create(handler);

        dispatcher.Publish(Publish("audit/login", "alice", retain: true));
        Assert.True(await WaitForAsync(() => dispatcher.Delivered == 1));

        var body = Assert.Single(handler.Bodies);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("audit/login", document.RootElement.GetProperty("topic").GetString());
        Assert.Equal("alice", System.Text.Encoding.UTF8.GetString(
            Convert.FromBase64String(document.RootElement.GetProperty("payload").GetString()!)));
        Assert.True(document.RootElement.GetProperty("retain").GetBoolean());
    }

    [Fact]
    public async Task Non_matching_topics_are_ignored()
    {
        var handler = new CaptureHandler();
        await using var dispatcher = Create(handler);

        dispatcher.Publish(Publish("other/topic", "x"));

        await Task.Delay(300);

        Assert.Empty(handler.Bodies);
        Assert.Equal(0, dispatcher.Delivered);
    }

    [Fact]
    public async Task Failed_post_is_retried()
    {
        var handler = new CaptureHandler(HttpStatusCode.InternalServerError, HttpStatusCode.InternalServerError);
        await using var dispatcher = Create(handler);

        dispatcher.Publish(Publish("audit/retry", "x"));

        Assert.True(await WaitForAsync(() => dispatcher.Delivered == 1));
        Assert.Equal(3, handler.Bodies.Count);
    }

    [Fact]
    public async Task Exhausted_retries_are_counted_as_failed()
    {
        var handler = new CaptureHandler(
            HttpStatusCode.BadGateway,
            HttpStatusCode.BadGateway,
            HttpStatusCode.BadGateway);
        await using var dispatcher = Create(handler);

        dispatcher.Publish(Publish("audit/fail", "x"));

        Assert.True(await WaitForAsync(() => dispatcher.Failed == 1));
        Assert.Equal(3, handler.Bodies.Count);
    }
}
