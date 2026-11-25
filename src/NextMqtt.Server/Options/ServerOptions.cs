namespace NextMqtt.Server.Options;

/// <summary>Network listeners and HTTP endpoints.</summary>
/// <remarks>各监听器可独立开关；端口冲突要到启动阶段才报错。</remarks>
public sealed class ServerOptions
{
    /// <summary>Configuration section name (<c>Server</c> / <c>NEXTMQTT_SERVER__</c>).</summary>
    /// <remarks>环境变量用双下划线分层，子段继续下探。</remarks>
    public const string SectionName = "Server";

    /// <summary>Seconds to wait for inflight QoS acknowledgements on shutdown.</summary>
    /// <remarks>0 表示不等待，直接断开。</remarks>
    public int DrainTimeoutSeconds { get; set; } = 5;

    /// <summary>Raw MQTT listener.</summary>
    /// <remarks>默认开启，是唯一的明文入口。</remarks>
    public TcpListenerOptions Tcp { get; set; } = new();

    /// <summary>TLS MQTT listener.</summary>
    /// <remarks>默认关闭；开启后必须提供证书，否则启动失败。</remarks>
    public TlsListenerOptions Tls { get; set; } = new();

    /// <summary>MQTT over WebSocket, served by Kestrel.</summary>
    /// <remarks>由 HTTP 端口承载，不额外占用端口。</remarks>
    public WebSocketListenerOptions WebSocket { get; set; } = new();

    /// <summary>Management API, dashboard and the optional extra WebSocket endpoint.</summary>
    /// <remarks>API 与面板同端口，8083 专供浏览器 MQTT 客户端。</remarks>
    public HttpListenerOptions Http { get; set; } = new();
}

/// <summary>Base options for a raw TCP listener.</summary>
/// <remarks>只描述地址与端口，TLS 在这之上扩展。</remarks>
public class TcpListenerOptions
{
    /// <summary>Whether the listener is started.</summary>
    /// <remarks>关掉后该端口完全不监听。</remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>Address to bind; <c>0.0.0.0</c> accepts every interface.</summary>
    /// <remarks>只接受 IP 字面量，不接受主机名。</remarks>
    public string Host { get; set; } = "0.0.0.0";

    /// <summary>TCP port.</summary>
    /// <remarks>与 TLS 端口分开，避免明文与加密混用。</remarks>
    public int Port { get; set; } = 1883;
}

/// <summary>TLS listener options; disabled by default.</summary>
/// <remarks>证书缺失时启动直接失败，不会静默降级为明文。</remarks>
public sealed class TlsListenerOptions : TcpListenerOptions
{
    /// <summary>Creates TLS defaults: disabled, port 8883.</summary>
    /// <remarks>改端口后记得同步客户端配置。</remarks>
    public TlsListenerOptions()
    {
        Enabled = false;
        Port = 8883;
    }

    /// <summary>PFX/PKCS#12 or PEM certificate path.</summary>
    /// <remarks>.pem 会尝试加载同目录同名的 .key 作为私钥。</remarks>
    public string? CertificatePath { get; set; }

    /// <summary>PEM private key path; defaults to a sibling .key file.</summary>
    /// <remarks>PFX 不需要此项，证书与私钥在同一个文件里。</remarks>
    public string? CertificateKeyPath { get; set; }

    /// <summary>Host name the certificate is expected to serve, for a startup check.</summary>
    /// <remarks>只做校验与日志，不限制客户端连接；证书不覆盖该域名时会打警告。</remarks>
    public string? Domain { get; set; }

    /// <summary>Password of the PFX file, when it has one.</summary>
    /// <remarks>PEM 不需要；应放环境变量而不是配置文件。</remarks>
    public string? CertificatePassword { get; set; }

    /// <summary>Request a client certificate; validation uses platform defaults.</summary>
    /// <remarks>只要求客户端出示证书，不做自定义链校验。</remarks>
    public bool RequireClientCertificate { get; set; }
}

/// <summary>WebSocket listener path for MQTT.</summary>
/// <remarks>路径必须与浏览器客户端连接的 URL 一致。</remarks>
public sealed class WebSocketListenerOptions
{
    /// <summary>Whether <c>/mqtt</c> is served.</summary>
    /// <remarks>关闭后浏览器客户端全部无法连接。</remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>HTTP path that accepts the <c>mqtt</c> subprotocol.</summary>
    /// <remarks>只接受带 mqtt 子协议的升级请求。</remarks>
    public string Path { get; set; } = "/mqtt";
}

/// <summary>Kestrel HTTP endpoints used by the dashboard and API.</summary>
/// <remarks>三个端口可以只开其中一部分。</remarks>
public sealed class HttpListenerOptions
{
    /// <summary>Plain HTTP port for the API and dashboard.</summary>
    /// <remarks>生产环境应放在反向代理或 TLS 之后。</remarks>
    public int Port { get; set; } = 8080;

    /// <summary>Extra Kestrel endpoint used by browser MQTT clients.</summary>
    /// <remarks>与 API 分开是为了让浏览器直连而不暴露管理接口。</remarks>
    public int WebSocketPort { get; set; } = 8083;

    /// <summary>HTTPS port when a certificate is configured; null disables HTTPS.</summary>
    /// <remarks>为 null 时该端口完全不监听。</remarks>
    public int? TlsPort { get; set; } = 8084;

    /// <summary>Certificate path for HTTPS.</summary>
    /// <remarks>与 MQTT TLS 证书相互独立，可以各用一套。</remarks>
    public string? CertificatePath { get; set; }

    /// <summary>PEM private key path for HTTPS; defaults to a sibling .key file.</summary>
    /// <remarks>PFX 不需要此项。</remarks>
    public string? CertificateKeyPath { get; set; }

    /// <summary>Host name the dashboard certificate is expected to serve.</summary>
    /// <remarks>只做启动校验与日志，不影响客户端连接。</remarks>
    public string? Domain { get; set; }

    /// <summary>Password of the HTTPS PFX file.</summary>
    /// <remarks>只有 PFX 需要，PEM 忽略此项。</remarks>
    public string? CertificatePassword { get; set; }

    /// <summary>CORS allow-list for the dashboard API.</summary>
    /// <remarks>开发时指向 Vite 源；生产同源部署可以留空。</remarks>
    public List<string> AllowedOrigins { get; set; } = ["http://localhost:5173"];
}

/// <summary>Management API and bootstrap credentials.</summary>
/// <remarks>只影响面板与 API，不影响 MQTT 客户端的认证。</remarks>
public sealed class AdminOptions
{
    /// <summary>Configuration section name (<c>Admin</c>).</summary>
    /// <remarks>环境变量前缀 NEXTMQTT_ADMIN__。</remarks>
    public const string SectionName = "Admin";

    /// <summary>Dashboard administrator username.</summary>
    /// <remarks>环境变量 NEXTMQTT_ADMIN_USERNAME 可以覆盖。</remarks>
    public string Username { get; set; } = "admin";

    /// <summary>PBKDF2 hash. Prefer the NEXTMQTT_ADMIN_PASSWORD environment variable.</summary>
    /// <remarks>明文密码在启动时现算哈希，配置文件里不留可用凭据。</remarks>
    public string? PasswordHash { get; set; }

    /// <summary>HMAC secret used to sign dashboard tokens; empty means an ephemeral key.</summary>
    /// <remarks>临时密钥会让重启后已签发的令牌全部失效。</remarks>
    public string? TokenSecret { get; set; }

    /// <summary>Dashboard token lifetime in minutes.</summary>
    /// <remarks>运行时会被夹在 5 分钟到 7 天之间。</remarks>
    public int TokenLifetimeMinutes { get; set; } = 480;

    /// <summary>SHA-256 hex digests of accepted API keys.</summary>
    /// <remarks>只存摘要；比较时不区分大小写。</remarks>
    public List<string> ApiKeySha256 { get; set; } = [];

    /// <summary>Require a token or API key for <c>/metrics</c>.</summary>
    /// <remarks>默认开放，便于 Prometheus 直接抓取。</remarks>
    public bool MetricsRequireAuth { get; set; }
}
