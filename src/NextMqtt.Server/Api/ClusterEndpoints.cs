using System.Text;
using System.Text.Json;
using NextMqtt.Broker.Clustering;

namespace NextMqtt.Server.Api;

/// <summary>Signed node-to-node endpoints. Not exposed to MQTT clients.</summary>
/// <summary>Internal endpoints used by peer nodes for heartbeats and forwarding.</summary>
public static class ClusterEndpoints
{
    private const int MaxBodyBytes = 4 * 1024 * 1024;

    /// <summary>Maps <c>/internal/cluster/*</c> without dashboard authentication.</summary>
    /// <remarks>这些端点由 HMAC 签名保护，不参与管理 API 的 Bearer 认证。</remarks>
    /// <param name="app">Application to extend.</param>
    /// <param name="router">Router that implements the handlers.</param>
    public static void Map(WebApplication app, ClusterRouter router)
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
    }

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
