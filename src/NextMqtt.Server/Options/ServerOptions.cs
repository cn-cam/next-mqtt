namespace NextMqtt.Server.Options;

/// <summary>Network listeners and HTTP endpoints.</summary>
public sealed class ServerOptions
{
    public const string SectionName = "Server";

    public TcpListenerOptions Tcp { get; set; } = new();

    public TlsListenerOptions Tls { get; set; } = new();

    public WebSocketListenerOptions WebSocket { get; set; } = new();

    public HttpListenerOptions Http { get; set; } = new();
}

public class TcpListenerOptions
{
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "0.0.0.0";

    public int Port { get; set; } = 1883;
}

public sealed class TlsListenerOptions : TcpListenerOptions
{
    public TlsListenerOptions()
    {
        Enabled = false;
        Port = 8883;
    }

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    public bool RequireClientCertificate { get; set; }
}

public sealed class WebSocketListenerOptions
{
    public bool Enabled { get; set; } = true;

    public string Path { get; set; } = "/mqtt";
}

public sealed class HttpListenerOptions
{
    public int Port { get; set; } = 8080;

    /// <summary>Extra Kestrel endpoint used by browser MQTT clients.</summary>
    public int WebSocketPort { get; set; } = 8083;

    public int? TlsPort { get; set; } = 8084;

    public string? CertificatePath { get; set; }

    public string? CertificatePassword { get; set; }

    public List<string> AllowedOrigins { get; set; } = ["http://localhost:5173"];
}

/// <summary>Management API and bootstrap credentials.</summary>
public sealed class AdminOptions
{
    public const string SectionName = "Admin";

    public string Username { get; set; } = "admin";

    /// <summary>PBKDF2 hash. Prefer the NEXTMQTT_ADMIN_PASSWORD environment variable.</summary>
    public string? PasswordHash { get; set; }

    /// <summary>HMAC secret used to sign dashboard tokens.</summary>
    public string? TokenSecret { get; set; }

    public int TokenLifetimeMinutes { get; set; } = 480;

    /// <summary>SHA-256 hex digests of accepted API keys.</summary>
    public List<string> ApiKeySha256 { get; set; } = [];

    public bool MetricsRequireAuth { get; set; }
}
