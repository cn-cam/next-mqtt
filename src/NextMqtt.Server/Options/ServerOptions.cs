namespace NextMqtt.Server.Options;

/// <summary>Network listeners and HTTP endpoints.</summary>
public sealed class ServerOptions
{
    /// <summary>Configuration section name (<c>Server</c> / <c>NEXTMQTT_SERVER__</c>).</summary>
    public const string SectionName = "Server";

    /// <summary>Seconds to wait for inflight QoS acknowledgements on shutdown.</summary>
    public int DrainTimeoutSeconds { get; set; } = 5;

    /// <summary>Raw MQTT listener.</summary>
    public TcpListenerOptions Tcp { get; set; } = new();

    /// <summary>TLS MQTT listener.</summary>
    public TlsListenerOptions Tls { get; set; } = new();

    /// <summary>MQTT over WebSocket, served by Kestrel.</summary>
    public WebSocketListenerOptions WebSocket { get; set; } = new();

    /// <summary>Management API, dashboard and the optional extra WebSocket endpoint.</summary>
    public HttpListenerOptions Http { get; set; } = new();
}

/// <summary>Base options for a raw TCP listener.</summary>
public class TcpListenerOptions
{
    /// <summary>Whether the listener is started.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Address to bind; <c>0.0.0.0</c> accepts every interface.</summary>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>TCP port.</summary>
    public int Port { get; set; } = 1883;
}

/// <summary>TLS listener options; disabled by default.</summary>
public sealed class TlsListenerOptions : TcpListenerOptions
{
    /// <summary>Creates TLS defaults: disabled, port 8883.</summary>
    public TlsListenerOptions()
    {
        Enabled = false;
        Port = 8883;
    }

    /// <summary>PFX/PKCS#12 or PEM certificate path.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Password of the PFX file, when it has one.</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>Request a client certificate; validation uses platform defaults.</summary>
    public bool RequireClientCertificate { get; set; }
}

/// <summary>WebSocket listener path for MQTT.</summary>
public sealed class WebSocketListenerOptions
{
    /// <summary>Whether <c>/mqtt</c> is served.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>HTTP path that accepts the <c>mqtt</c> subprotocol.</summary>
    public string Path { get; set; } = "/mqtt";
}

/// <summary>Kestrel HTTP endpoints used by the dashboard and API.</summary>
public sealed class HttpListenerOptions
{
    /// <summary>Plain HTTP port for the API and dashboard.</summary>
    public int Port { get; set; } = 8080;

    /// <summary>Extra Kestrel endpoint used by browser MQTT clients.</summary>
    public int WebSocketPort { get; set; } = 8083;

    /// <summary>HTTPS port when a certificate is configured; null disables HTTPS.</summary>
    public int? TlsPort { get; set; } = 8084;

    /// <summary>Certificate path for HTTPS.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Password of the HTTPS PFX file.</summary>
    public string? CertificatePassword { get; set; }

    /// <summary>CORS allow-list for the dashboard API.</summary>
    public List<string> AllowedOrigins { get; set; } = ["http://localhost:5173"];
}

/// <summary>Management API and bootstrap credentials.</summary>
public sealed class AdminOptions
{
    /// <summary>Configuration section name (<c>Admin</c>).</summary>
    public const string SectionName = "Admin";

    /// <summary>Dashboard administrator username.</summary>
    public string Username { get; set; } = "admin";

    /// <summary>PBKDF2 hash. Prefer the NEXTMQTT_ADMIN_PASSWORD environment variable.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>HMAC secret used to sign dashboard tokens; empty means an ephemeral key.</summary>
    public string? TokenSecret { get; set; }

    /// <summary>Dashboard token lifetime in minutes.</summary>
    public int TokenLifetimeMinutes { get; set; } = 480;

    /// <summary>SHA-256 hex digests of accepted API keys.</summary>
    public List<string> ApiKeySha256 { get; set; } = [];

    /// <summary>Require a token or API key for <c>/metrics</c>.</summary>
    public bool MetricsRequireAuth { get; set; }
}
