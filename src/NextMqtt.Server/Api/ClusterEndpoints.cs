using System.Text;
using System.Text.Json;
using NextMqtt.Broker;
using NextMqtt.Broker.Clustering;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Server.Api;

/// <summary>Internal endpoints used by peer nodes for heartbeats and forwarding.</summary>
/// <remarks>走 HMAC 签名而不是管理令牌；请求体上限 4 MiB，超过直接回 413。</remarks>
public static class ClusterEndpoints
{
    /// <summary>Upper bound on a cluster request body; larger requests get 413.</summary>
    private const int MaxBodyBytes = 4 * 1024 * 1024;

    /// <summary>Maps <c>/internal/cluster/*</c> without dashboard authentication.</summary>
    /// <remarks>这些端点由 HMAC 签名保护，不参与管理 API 的 Bearer 认证。</remarks>
    /// <param name="app">Application to extend.</param>
    /// <param name="router">Router that implements the handlers.</param>
    /// <param name="broker">Broker that owns the retained store.</param>
    public static void Map(WebApplication app, ClusterRouter router, MqttBroker broker)
    {
        app.MapPost("/internal/cluster/heartbeat", async context =>
        {
            var body = await ReadBodyAsync(context);

            if (body is null)
            {
                return;
            }

            if (!Verify(context, router.Secret, body))
            {
                return;
            }

            ClusterHeartbeatRequest? request;

            try
            {
                request = JsonSerializer.Deserialize<ClusterHeartbeatRequest>(body, ClusterJson.Options);
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (request is null || string.IsNullOrEmpty(request.NodeId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var response = await router.ProcessHeartbeatAsync(request, context.RequestAborted);
            await context.Response.WriteAsJsonAsync(response, ClusterJson.Options);
        });

        app.MapPost("/internal/cluster/publish", async context =>
        {
            var body = await ReadBodyAsync(context);

            if (body is null)
            {
                return;
            }

            if (!Verify(context, router.Secret, body))
            {
                return;
            }

            ClusterPublishMessage? message;

            try
            {
                message = JsonSerializer.Deserialize<ClusterPublishMessage>(body, ClusterJson.Options);
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (message is null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var accepted = await router.ProcessPublishAsync(message, context.RequestAborted);
            context.Response.StatusCode = accepted
                ? StatusCodes.Status202Accepted
                : StatusCodes.Status409Conflict;
        });

        app.MapPost("/internal/cluster/leave", async context =>
        {
            var body = await ReadBodyAsync(context);

            if (body is null)
            {
                return;
            }

            if (!Verify(context, router.Secret, body))
            {
                return;
            }

            var nodeId = context.Request.Query["nodeId"].ToString();

            if (!string.IsNullOrEmpty(nodeId))
            {
                router.Leave(nodeId);
            }

            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });

        app.MapPost("/internal/cluster/retained", async context =>
        {
            var body = await ReadBodyAsync(context);

            if (body is null)
            {
                return;
            }

            if (!Verify(context, router.Secret, body))
            {
                return;
            }

            var now = DateTimeOffset.UtcNow;
            var snapshot = broker.Retained.Snapshot()
                .Where(message => !message.IsExpired(now))
                .Take(10_000)
                .Select(message => new ClusterRetainedMessage(
                    message.Topic,
                    Convert.ToBase64String(message.Payload),
                    (int)message.QoS,
                    message.StoredAt.ToUnixTimeSeconds(),
                    message.ExpiresAt?.ToUnixTimeSeconds(),
                    message.Properties.Items.Select(ClusterPropertyDto.FromProperty).ToArray()))
                .ToArray();

            await context.Response.WriteAsJsonAsync(snapshot, ClusterJson.Options);
        });

        app.MapPost("/internal/cluster/session/claim", async context =>
        {
            var body = await ReadBodyAsync(context);

            if (body is null)
            {
                return;
            }

            if (!Verify(context, router.Secret, body))
            {
                return;
            }

            ClusterSessionClaim? claim;

            try
            {
                claim = JsonSerializer.Deserialize<ClusterSessionClaim>(body, ClusterJson.Options);
            }
            catch (JsonException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            if (claim is null || string.IsNullOrEmpty(claim.ClientId))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            var transfer = await broker.DetachSessionAsync(claim.ClientId, context.RequestAborted);

            if (transfer is null)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await context.Response.WriteAsJsonAsync(transfer, ClusterJson.Options);
        });
    }

    /// <summary>Reads the body as UTF-8, returning null after answering 413 for an oversized request.</summary>
    private static async Task<string?> ReadBodyAsync(HttpContext context)
    {
        if (context.Request.ContentLength is > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return null;
        }

        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        var body = await reader.ReadToEndAsync(context.RequestAborted);

        if (body.Length > MaxBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            return null;
        }

        return body;
    }

    /// <summary>Checks the signature headers and answers 401 when they do not match.</summary>
    private static bool Verify(HttpContext context, string secret, string body)
    {
        var timestamp = context.Request.Headers[ClusterSignature.TimestampHeader].ToString();
        var signature = context.Request.Headers[ClusterSignature.SignatureHeader].ToString();

        if (ClusterSignature.Verify(secret, timestamp, signature, body, out _))
        {
            return true;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return false;
    }
}
