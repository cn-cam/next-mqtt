using System.Text;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.FileProviders;
using NextMqtt.Broker;
using NextMqtt.Broker.Clustering;
using NextMqtt.Broker.Connections;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Broker.Integrations;
using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Storage;
using NextMqtt.Server.Api;
using NextMqtt.Server.Diagnostics;
using NextMqtt.Server.Hosting;
using NextMqtt.Server.Networking;
using NextMqtt.Server.Options;
using NextMqtt.Server.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables("NEXTMQTT_");

var serverOptions = builder.Configuration.GetSection(ServerOptions.SectionName).Get<ServerOptions>() ?? new ServerOptions();
var brokerOptions = builder.Configuration.GetSection(BrokerOptions.SectionName).Get<BrokerOptions>() ?? new BrokerOptions();
var authenticationOptions = builder.Configuration.GetSection(AuthenticationOptions.SectionName).Get<AuthenticationOptions>() ?? new AuthenticationOptions();
var aclOptions = builder.Configuration.GetSection(AclOptions.SectionName).Get<AclOptions>() ?? new AclOptions();
var adminOptions = builder.Configuration.GetSection(AdminOptions.SectionName).Get<AdminOptions>() ?? new AdminOptions();
var clusterOptions = builder.Configuration.GetSection(ClusterOptions.SectionName).Get<ClusterOptions>() ?? new ClusterOptions();
brokerOptions.RateLimits = builder.Configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>()
    ?? brokerOptions.RateLimits;
var storageOptions = builder.Configuration.GetSection(StorageOptions.SectionName).Get<StorageOptions>()
    ?? new StorageOptions();
var integrationsOptions = builder.Configuration.GetSection(IntegrationsOptions.SectionName).Get<IntegrationsOptions>()
    ?? new IntegrationsOptions();

var enhancedAuthenticators = new List<IEnhancedAuthenticator>();

if (authenticationOptions.Jwt.Enabled)
{
    enhancedAuthenticators.Add(new JwtEnhancedAuthenticator(authenticationOptions.Jwt));
}

if (bool.TryParse(Environment.GetEnvironmentVariable("NEXTMQTT_ALLOW_ANONYMOUS"), out var allowAnonymous))
{
    authenticationOptions.AllowAnonymous = allowAnonymous;
}

MqttCertificateLoader.CertificateBundle? httpsBundle = null;

builder.WebHost.ConfigureKestrel(kestrel =>
{
    kestrel.AddServerHeader = false;
    kestrel.Limits.MaxRequestBodySize = 16 * 1024 * 1024;
    kestrel.ListenAnyIP(serverOptions.Http.Port);

    if (serverOptions.Http.WebSocketPort > 0 && serverOptions.Http.WebSocketPort != serverOptions.Http.Port)
    {
        kestrel.ListenAnyIP(serverOptions.Http.WebSocketPort);
    }

    if (serverOptions.Http.TlsPort is { } tlsPort && !string.IsNullOrEmpty(serverOptions.Http.CertificatePath))
    {
        httpsBundle = MqttCertificateLoader.Load(
            serverOptions.Http.CertificatePath,
            serverOptions.Http.CertificateKeyPath,
            serverOptions.Http.CertificatePassword);

        kestrel.ListenAnyIP(tlsPort, listen => listen.UseHttps(new HttpsConnectionAdapterOptions
        {
            ServerCertificate = httpsBundle.Certificate,
            ServerCertificateChain = httpsBundle.Chain,
        }));
    }
});

builder.Services.AddProblemDetails();
builder.Services.Configure<JsonOptions>(json =>
{
    json.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    json.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("dashboard", policy =>
    {
        if (serverOptions.Http.AllowedOrigins.Count > 0)
        {
            policy.WithOrigins(serverOptions.Http.AllowedOrigins.ToArray())
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});

builder.Services.AddSingleton(serverOptions);
builder.Services.AddSingleton(brokerOptions);
builder.Services.AddSingleton(adminOptions);

var logBuffer = new LogBuffer(2000);
builder.Logging.AddProvider(new LogBufferProvider(logBuffer));
builder.Services.AddSingleton(logBuffer);

var builtinAuthenticator = new ClientAuthenticator(authenticationOptions);
var builtinAuthorizer = new TopicAuthorizer(aclOptions);
var switchableAuthenticator = new SwitchableAuthenticator(builtinAuthenticator);
var switchableAuthorizer = new SwitchableAuthorizer(builtinAuthorizer);

builder.Services.AddSingleton(switchableAuthenticator);
builder.Services.AddSingleton<IClientAuthenticator>(switchableAuthenticator);
builder.Services.AddSingleton(switchableAuthorizer);
builder.Services.AddSingleton<ITopicAuthorizer>(switchableAuthorizer);
builder.Services.AddSingleton(sp => new PluginRuntime(
    () => builder.Configuration.GetSection(PluginOptions.SectionName).Get<PluginOptions>()
        ?? new PluginOptions(),
    sp.GetRequiredService<ILoggerFactory>(),
    switchableAuthenticator,
    switchableAuthorizer,
    builtinAuthenticator,
    builtinAuthorizer));
builder.Services.AddSingleton(clusterOptions);
builder.Services.AddSingleton(_ => new HttpClient
{
    Timeout = TimeSpan.FromSeconds(Math.Max(1, clusterOptions.RequestTimeoutSeconds)),
});
builder.Services.AddSingleton(sp => new ClusterRouter(
    clusterOptions,
    sp.GetRequiredService<ILogger<ClusterRouter>>(),
    sp.GetRequiredService<HttpClient>()));
builder.Services.AddSingleton<IClusterRouter>(sp => sp.GetRequiredService<ClusterRouter>());

if (storageOptions.Retained.Mode == StorageMode.File)
{
    builder.Services.AddSingleton<IRetainedJournal>(sp =>
    {
        var journal = new FileRetainedJournal(
            storageOptions.Retained.Path,
            storageOptions.Retained.CompactThresholdBytes);

        sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(journal.Dispose);
        return journal;
    });
}

if (storageOptions.Replicas.Mode == StorageMode.File)
{
    builder.Services.AddSingleton<ISessionReplicaStore>(
        _ => new FileSessionReplicaStore(
            storageOptions.Replicas.Path,
            storageOptions.Replicas.ShardCount));
}

if (integrationsOptions.Webhooks.Count > 0)
{    builder.Services.AddSingleton(sp =>
    {
        var dispatcher = new WebhookDispatcher(
            integrationsOptions,
            new HttpClient { Timeout = Timeout.InfiniteTimeSpan },
            sp.GetRequiredService<ILogger<WebhookDispatcher>>());

        sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
            dispatcher.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)));

        return dispatcher;
    });
}

if (integrationsOptions.Nats.Enabled)
{
    builder.Services.AddSingleton(sp =>
    {
        var bridge = new NatsBridge(
            integrationsOptions.Nats,
            sp.GetRequiredService<ILogger<NatsBridge>>());

        sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
            bridge.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(2)));

        return bridge;
    });
}

if (integrationsOptions.Kafka.Enabled)
{
    builder.Services.AddSingleton(sp =>
    {
        var bridge = new KafkaBridge(
            integrationsOptions.Kafka,
            sp.GetRequiredService<ILogger<KafkaBridge>>());

        sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
            bridge.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)));

        return bridge;
    });
}

if (integrationsOptions.Rabbit.Enabled)
{
    builder.Services.AddSingleton(sp =>
    {
        var bridge = new RabbitBridge(
            integrationsOptions.Rabbit,
            sp.GetRequiredService<ILogger<RabbitBridge>>());

        sp.GetRequiredService<IHostApplicationLifetime>().ApplicationStopping.Register(() =>
            bridge.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3)));

        return bridge;
    });
}

if (integrationsOptions.Rules.Count > 0)
{
    builder.Services.AddSingleton(sp => new RuleEngine(
        integrationsOptions,
        sp.GetRequiredService<ILogger<RuleEngine>>()));
}

if (storageOptions.Session.Mode == StorageMode.File)
{
    builder.Services.AddSingleton<ISessionSnapshotStore>(
        new FileSessionSnapshotStore(storageOptions.Session.Path));
}

builder.Services.AddSingleton(sp =>
{
    var cluster = sp.GetRequiredService<IClusterRouter>();
    var broker = new MqttBroker(
        brokerOptions,
        sp.GetRequiredService<IClientAuthenticator>(),
        sp.GetRequiredService<ITopicAuthorizer>(),
        cluster,
        sp.GetRequiredService<ILogger<MqttBroker>>(),
        sp.GetService<IRetainedJournal>(),
        sp.GetService<ISessionSnapshotStore>(),
        storageOptions.Session.SnapshotSeconds,
        enhancedAuthenticators,
        sp.GetService<WebhookDispatcher>(),
        sp.GetService<RuleEngine>(),
        sp.GetService<NatsBridge>(),
        sp.GetService<KafkaBridge>(),
        sp.GetService<RabbitBridge>(),
        sp.GetService<ISessionReplicaStore>());

    if (cluster is ClusterRouter clusterRouter)
    {
        clusterRouter.AttachBroker(broker);
    }

    return broker;
});
builder.Services.AddSingleton(sp => AdminAuthService.Create(
    adminOptions,
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<AdminAuthService>()));
builder.Services.AddSingleton<BrokerLifecycleService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<BrokerLifecycleService>());
builder.Services.AddHostedService<MqttTcpListenerService>();

var app = builder.Build();
var broker = app.Services.GetRequiredService<MqttBroker>();

// Construct the plugin runtime before serving: plugins must be active from the
// first connection, not from the first /plugins request.
app.Services.GetRequiredService<PluginRuntime>();
var adminAuth = app.Services.GetRequiredService<AdminAuthService>();
var clusterRouter = app.Services.GetRequiredService<ClusterRouter>();
var version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.1.2";

if (httpsBundle is not null)
{
    var httpsDomain = serverOptions.Http.Domain;

    if (MqttCertificateLoader.CoversDomain(httpsBundle.Certificate, httpsDomain))
    {
        app.Logger.LogInformation(
            "HTTPS certificate serves {Domain}, valid until {Expiry:u}",
            httpsDomain,
            httpsBundle.Certificate.NotAfter);
    }
    else if (!string.IsNullOrWhiteSpace(httpsDomain))
    {
        app.Logger.LogWarning(
            "HTTPS certificate does not cover {Domain}; browsers will reject it unless the certificate lists that name",
            httpsDomain);
    }
}

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(20),
});

app.UseCors("dashboard");

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'; worker-src 'self' blob:; connect-src 'self' ws: wss:; font-src 'self'; base-uri 'none'; frame-ancestors 'none'";

    if (context.Request.Path.StartsWithSegments("/api/v1") && !IsPublicApi(context.Request.Path))
    {
        var principal = adminAuth.Authenticate(context.Request);

        if (principal is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(
                new ApiError("unauthorized", "A valid bearer token or API key is required."));
            return;
        }

        context.Items["principal"] = principal;
    }

    await next();
});

app.UseDefaultFiles();
app.UseStaticFiles();

if (clusterRouter.Enabled)
{
    ClusterEndpoints.Map(app, clusterRouter, broker);
}

var dashboardDist = Path.GetFullPath(Path.Combine(app.Environment.ContentRootPath, "..", "..", "web", "dist"));

if (Directory.Exists(dashboardDist))
{
    app.UseDefaultFiles(new DefaultFilesOptions
    {
        FileProvider = new PhysicalFileProvider(dashboardDist),
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(dashboardDist),
    });
}

if (serverOptions.WebSocket.Enabled)
{
    app.Map(serverOptions.WebSocket.Path, async context =>
    {        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("This endpoint expects a WebSocket upgrade.");
            return;
        }

        var subProtocol = context.WebSockets.WebSocketRequestedProtocols.Contains("mqtt", StringComparer.OrdinalIgnoreCase)
            ? "mqtt"
            : null;

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync(subProtocol);
        await using var stream = new WebSocketStream(webSocket);

        var connection = new ClientConnection(
            stream,
            context.Connection.RemoteIpAddress?.ToString() ?? "websocket",
            broker,
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger<ClientConnection>());

        await connection.RunAsync(context.RequestAborted);
    });
}

var api = app.MapGroup("/api/v1");

api.MapGet("/health", () => Results.Ok(new { status = "ok", version }));
api.MapGet("/ready", (MqttBroker b) => Results.Ok(new { status = "ready", sessions = b.Sessions.Count }));
api.MapGet("/version", () => Results.Ok(new VersionInfo(
    "NextMQTT",
    version,
    System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    app.Environment.EnvironmentName)));

api.MapPost("/auth/login", (LoginRequest request, AdminAuthService auth) =>
{
    if (string.IsNullOrEmpty(request.Username)
        || string.IsNullOrEmpty(request.Password)
        || !auth.ValidateCredentials(request.Username, request.Password))
    {
        return Results.Json(
            new ApiError("invalid_credentials", "Invalid username or password."),
            statusCode: StatusCodes.Status401Unauthorized);
    }

    var token = auth.IssueToken(out var expiresAt);
    return Results.Ok(new LoginResponse(token, expiresAt, request.Username));
});

api.MapGet("/auth/me", (HttpContext context) =>
{
    var principal = context.Items["principal"] as ClientPrincipal;
    return Results.Ok(new
    {
        username = principal?.Username,
        superuser = principal?.IsSuperuser ?? false,
    });
});

api.MapGet("/stats", (MqttBroker b) => Results.Ok(b.GetStats()));

api.MapGet("/clients", (MqttBroker b, string? search, int page = 1, int pageSize = 50) =>
{
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 200);

    var query = b.GetClients().AsEnumerable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        query = query.Where(client =>
            client.ClientId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || (client.Username?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false)
            || (client.RemoteAddress?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
    }

    var filtered = query.ToList();
    var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

    return Results.Ok(new PagedResponse<NextMqtt.Broker.Diagnostics.ClientSnapshot>(
        items, filtered.Count, page, pageSize));
});

api.MapGet("/clients/{clientId}", (string clientId, MqttBroker b) =>
{
    var client = b.GetClients().FirstOrDefault(item => string.Equals(item.ClientId, clientId, StringComparison.Ordinal));
    return client is null ? Results.NotFound() : Results.Ok(client);
});

api.MapDelete("/clients/{clientId}", async (string clientId, MqttBroker b, CancellationToken cancellationToken) =>
    await b.KickClientAsync(clientId, cancellationToken)
        ? Results.NoContent()
        : Results.NotFound());

api.MapGet("/subscriptions", (MqttBroker b, string? search, int page = 1, int pageSize = 100) =>
{
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 500);

    var query = b.GetSubscriptions().AsEnumerable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        query = query.Where(subscription =>
            subscription.ClientId.Contains(search, StringComparison.OrdinalIgnoreCase)
            || subscription.Filter.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    var filtered = query.ToList();
    var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

    return Results.Ok(new PagedResponse<NextMqtt.Broker.Diagnostics.SubscriptionSnapshot>(
        items, filtered.Count, page, pageSize));
});

api.MapGet("/retained", (MqttBroker b, string? search, int page = 1, int pageSize = 100) =>
{
    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize, 1, 500);

    var query = b.GetRetained().AsEnumerable();

    if (!string.IsNullOrWhiteSpace(search))
    {
        query = query.Where(message => message.Topic.Contains(search, StringComparison.OrdinalIgnoreCase));
    }

    var filtered = query.ToList();
    var items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

    return Results.Ok(new PagedResponse<NextMqtt.Broker.Diagnostics.RetainedSnapshot>(
        items, filtered.Count, page, pageSize));
});

api.MapDelete("/retained", (string topic, MqttBroker b) =>
    b.DeleteRetained(topic) ? Results.NoContent() : Results.NotFound());

api.MapPost("/publish", async (PublishRequest request, MqttBroker b, CancellationToken cancellationToken) =>
{
    if (request.QoS is < 0 or > 2)
    {
        return Results.BadRequest(new ApiError("invalid_qos", "QoS must be 0, 1 or 2."));
    }

    byte[] payload;

    try
    {
        payload = string.Equals(request.Encoding, "base64", StringComparison.OrdinalIgnoreCase)
            ? Convert.FromBase64String(request.Payload ?? string.Empty)
            : Encoding.UTF8.GetBytes(request.Payload ?? string.Empty);
    }
    catch (FormatException)
    {
        return Results.BadRequest(new ApiError("invalid_payload", "Payload is not valid base64."));
    }

    try
    {
        await b.PublishAsync(request.Topic, payload, (NextMqtt.Core.Protocol.MqttQoS)request.QoS, request.Retain, cancellationToken);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new ApiError("invalid_topic", ex.Message));
    }

    return Results.Ok(new { topic = request.Topic, bytes = payload.Length, qos = request.QoS, retain = request.Retain });
});

api.MapGet("/cluster", (MqttBroker b) => Results.Ok(b.GetCluster()));

api.MapGet("/logs", (LogBuffer buffer, string? level, string? search, int limit = 200) =>
{
    var floor = LogBuffer.ParseLevel(level);
    var entries = buffer.Snapshot(floor, search, limit);

    return Results.Ok(new
    {
        entries,
        total = buffer.Total,
        capacity = buffer.Capacity,
        level = floor ?? "Trace",
    });
});

api.MapGet("/plugins", (PluginRuntime runtime) => Results.Ok(runtime.Status()));

api.MapPost("/plugins/reload", (PluginRuntime runtime) =>
{
    try
    {
        return Results.Ok(runtime.Reload());
    }
    catch (Exception ex) when (ex is IOException or BadImageFormatException or System.Text.Json.JsonException)
    {
        return Results.Problem(
            title: "Plugin reload failed",
            detail: ex.Message,
            statusCode: StatusCodes.Status500InternalServerError);
    }
});

api.MapGet("/backup", (MqttBroker b) => Results.Ok(b.CreateBackup()));

api.MapPost("/backup", (MqttBroker b, BrokerBackup backup) =>
{
    var result = b.RestoreBackup(backup);
    return Results.Ok(new { retained = result.Retained, sessions = result.Sessions });
});

api.MapGet("/config", (MqttBroker b) => Results.Ok(new
{
    server = new
    {
        tcp = new { serverOptions.Tcp.Enabled, serverOptions.Tcp.Host, serverOptions.Tcp.Port },
        tls = new { serverOptions.Tls.Enabled, serverOptions.Tls.Port, serverOptions.Tls.Domain },
        websocket = new { serverOptions.WebSocket.Enabled, serverOptions.WebSocket.Path },
        http = new
        {
            serverOptions.Http.Port,
            serverOptions.Http.WebSocketPort,
            serverOptions.Http.TlsPort,
            serverOptions.Http.Domain,
        },
    },
    broker = new
    {
        brokerOptions.MaxPacketSize,
        brokerOptions.MaxInflightMessages,
        brokerOptions.MaxQueuedMessages,
        brokerOptions.MaxSubscriptionsPerSession,
        brokerOptions.MaxTopicAlias,
    },
    cluster = b.GetCluster(),
}));

app.MapGet("/metrics", (HttpContext context) =>
{
    if (adminOptions.MetricsRequireAuth && adminAuth.Authenticate(context.Request) is null)
    {
        return Results.Unauthorized();
    }

    var text = PrometheusFormatter.Format(broker.GetStats(), Math.Max(1, broker.GetCluster().Nodes.Count), version);
    return Results.Text(text, "text/plain; version=0.0.4; charset=utf-8");
});

app.MapGet("/healthz", () => Results.Ok(new { status = "ok", version }));
app.MapGet("/readyz", (MqttBroker b) => Results.Ok(new { status = "ready", sessions = b.Sessions.Count }));

app.MapFallback(async context =>
{
    if (context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/mqtt"))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    var webRoot = app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot");
    var index = new[]
        {
            Path.Combine(webRoot, "index.html"),
            Path.Combine(dashboardDist, "index.html"),
        }
        .FirstOrDefault(File.Exists);

    if (index is not null)
    {
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.SendFileAsync(index);
        return;
    }

    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.WriteAsync(
        """
        <!doctype html>
        <html lang="en">
        <head><meta charset="utf-8"><title>NextMQTT</title>
        <style>body{font-family:system-ui,sans-serif;background:#0b1020;color:#e6edf7;display:grid;place-items:center;height:100vh;margin:0}main{max-width:34rem;padding:2rem}a{color:#6ee7ff}</style>
        </head>
        <body><main>
        <h1>NextMQTT is running</h1>
        <p>The dashboard bundle is not built yet. Start the UI with:</p>
        <pre>cd web
        npm install
        npm run dev</pre>
        <p>API: <a href="/api/v1/stats">/api/v1/stats</a> · Metrics: <a href="/metrics">/metrics</a></p>
        </main></body></html>
        """);
});

app.Run();

static bool IsPublicApi(PathString path)
    => path.StartsWithSegments("/api/v1/health")
       || path.StartsWithSegments("/api/v1/ready")
       || path.StartsWithSegments("/api/v1/version")
       || path == "/api/v1/auth/login";

/// <summary>Entry point marker used by integration tests and tooling.</summary>
/// <remarks>测试用 WebApplicationFactory 找程序集入口，不要删。</remarks>
public partial class Program;

