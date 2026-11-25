using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using NextMqtt.Broker;
using NextMqtt.Broker.Connections;
using NextMqtt.Server.Options;
using Microsoft.Extensions.Logging;

namespace NextMqtt.Server.Networking;

/// <summary>Loads PFX/PKCS#12 or PEM certificates for MQTT/TLS listeners.</summary>
/// <remarks>PEM 优先使用显式给定的私钥路径，其次找同目录同名的 .key；证书链随叶子证书一起返回。</remarks>
public static class MqttCertificateLoader
{
    /// <summary>Certificate plus the intermediates that must be sent with it.</summary>
    /// <remarks>链只用于补齐发送给客户端的中间证书，不会影响私钥选择。</remarks>
    /// <param name="Certificate">Server certificate, with its private key.</param>
    /// <param name="Chain">Intermediate certificates, or null when the file had none.</param>
    public sealed record CertificateBundle(X509Certificate2 Certificate, X509Certificate2Collection? Chain);

    /// <summary>Key storage that the platform can actually serve from.</summary>
    /// <remarks>Windows 的 SChannel 不支持临时密钥，只能用品控里持久化的用户密钥，证书释放时随之删除。</remarks>
    private static X509KeyStorageFlags KeyStorageFlags =>
        OperatingSystem.IsWindows() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;

    /// <summary>Loads a PFX/PEM certificate from disk.</summary>
    /// <param name="path">Certificate file path.</param>
    /// <param name="keyPath">PEM private key path, or null to look for a sibling .key file.</param>
    /// <param name="password">PFX password, when required.</param>
    /// <remarks>PEM 会附带中间证书；Windows 上还会把临时密钥转成服务可用的形式。</remarks>
    public static CertificateBundle Load(string path, string? keyPath, string? password)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Certificate file was not found.", path);
        }

        var isPem = path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase) || keyPath is not null;

        if (!isPem)
        {
            var pfx = X509CertificateLoader.LoadPkcs12FromFile(path, password, KeyStorageFlags);
            return new CertificateBundle(pfx, null);
        }

        var certificates = new X509Certificate2Collection();
        certificates.ImportFromPemFile(path);

        if (certificates.Count == 0)
        {
            throw new InvalidDataException($"No certificate found in {path}.");
        }

        var privateKeyPath = keyPath ?? Path.ChangeExtension(path, ".key");
        var leaf = certificates[0];

        if (File.Exists(privateKeyPath))
        {
            leaf = AttachPrivateKey(certificates, File.ReadAllText(privateKeyPath));
            leaf = MakeUsableOnWindows(leaf);
        }
        else if (!leaf.HasPrivateKey)
        {
            throw new InvalidOperationException(
                $"Certificate {path} has no private key. Set the key path or place the key next to it as {Path.GetFileName(privateKeyPath)}.");
        }

        X509Certificate2Collection? chain = null;

        foreach (var candidate in certificates)
        {
            if (!string.Equals(candidate.Thumbprint, leaf.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                (chain ??= []).Add(candidate);
            }
        }

        return new CertificateBundle(leaf, chain);
    }

    /// <summary>True when the certificate covers the host name, wildcards included.</summary>
    /// <remarks>只比较单层通配符；IP 地址与空域名一律返回 false。</remarks>
    /// <param name="certificate">Server certificate.</param>
    /// <param name="domain">Host name clients will use.</param>
    public static bool CoversDomain(X509Certificate2 certificate, string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || IPAddress.TryParse(domain, out _))
        {
            return false;
        }

        var names = new List<string>();

        foreach (var extension in certificate.Extensions)
        {
            if (extension is X509SubjectAlternativeNameExtension san)
            {
                names.AddRange(san.EnumerateDnsNames());
            }
        }

        if (names.Count == 0)
        {
            names.Add(certificate.GetNameInfo(X509NameType.DnsName, forIssuer: false));
        }

        foreach (var name in names)
        {
            if (string.Equals(name, domain, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (name.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = name[1..];

                if (domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                    && domain.Length > suffix.Length
                    && domain[..^suffix.Length].IndexOf('.') < 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Matches the PEM key to one of the certificates, RSA first then ECDSA.</summary>
    private static X509Certificate2 AttachPrivateKey(X509Certificate2Collection certificates, string pem)
    {
        foreach (var certificate in certificates)
        {
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportFromPem(pem);
                return certificate.CopyWithPrivateKey(rsa);
            }
            catch (CryptographicException)
            {
                // Not an RSA key; try the other algorithm.
            }

            try
            {
                using var ecdsa = ECDsa.Create();
                ecdsa.ImportFromPem(pem);
                return certificate.CopyWithPrivateKey(ecdsa);
            }
            catch (CryptographicException)
            {
                // Try the next certificate in the file.
            }
        }

        throw new InvalidOperationException("The private key does not match any certificate in the file.");
    }

    /// <summary>Re-imports the key through PKCS#12 because SChannel cannot serve an ephemeral one.</summary>
    private static X509Certificate2 MakeUsableOnWindows(X509Certificate2 certificate)
    {
        if (!OperatingSystem.IsWindows() || !certificate.HasPrivateKey)
        {
            return certificate;
        }

        // SChannel cannot serve an ephemeral key that was copied from PEM, so
        // round-trip through PKCS#12, which materialises a usable key handle.
        var pfx = certificate.Export(X509ContentType.Pkcs12);
        return X509CertificateLoader.LoadPkcs12(pfx, null, KeyStorageFlags);
    }
}

/// <summary>Accepts MQTT connections on raw TCP and TLS sockets.</summary>
/// <remarks>每个启用的监听器一条 accept 循环；关闭时先停止接收，再等待 inflight 确认。</remarks>
public sealed class MqttTcpListenerService : BackgroundService
{
    /// <summary>Broker that owns the accepted connections.</summary>
    private readonly MqttBroker _broker;
    /// <summary>Listener, TLS and drain settings.</summary>
    private readonly ServerOptions _options;
    /// <summary>Logger for listener and connection errors.</summary>
    private readonly ILogger<MqttTcpListenerService> _logger;
    /// <summary>Factory used to give each connection its own logger.</summary>
    private readonly ILoggerFactory _loggerFactory;
    /// <summary>Started listeners, stopped first during shutdown.</summary>
    private readonly List<TcpListener> _listeners = [];
    /// <summary>Loaded certificates, disposed after the listeners stop.</summary>
    private readonly List<MqttCertificateLoader.CertificateBundle> _certificates = [];
    /// <summary>Running connection tasks, awaited during shutdown.</summary>
    private readonly List<Task> _connections = [];
    /// <summary>Guards the listener and connection lists.</summary>
    private readonly object _connectionGate = new();

    /// <summary>Creates the listener service.</summary>
    /// <remarks>构造时不绑定端口，端口冲突要到启动阶段才暴露。</remarks>
    /// <param name="broker">Broker that owns the connections.</param>
    /// <param name="options">Listener and drain settings.</param>
    /// <param name="logger">Listener logger.</param>
    /// <param name="loggerFactory">Factory for per-connection loggers.</param>
    public MqttTcpListenerService(
        MqttBroker broker,
        ServerOptions options,
        ILogger<MqttTcpListenerService> logger,
        ILoggerFactory loggerFactory)
    {
        _broker = broker;
        _options = options;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    /// <summary>Runs one accept loop per enabled listener until shutdown.</summary>
    /// <remarks>TLS 开启但没配证书会在这里失败，而不是静默跳过。</remarks>
    /// <param name="stoppingToken">Cancels the accept loops.</param>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var loops = new List<Task>();

        if (_options.Tcp.Enabled)
        {
            loops.Add(RunListenerAsync(_options.Tcp, certificate: null, requireClientCertificate: false, stoppingToken));
        }

        if (_options.Tls.Enabled)
        {
            if (string.IsNullOrEmpty(_options.Tls.CertificatePath))
            {
                throw new InvalidOperationException("TLS listener is enabled but Server:Tls:CertificatePath is not configured.");
            }

            var certificate = MqttCertificateLoader.Load(
                _options.Tls.CertificatePath,
                _options.Tls.CertificateKeyPath,
                _options.Tls.CertificatePassword);

            if (!string.IsNullOrWhiteSpace(_options.Tls.Domain)
                && !MqttCertificateLoader.CoversDomain(certificate.Certificate, _options.Tls.Domain))
            {
                _logger.LogWarning(
                    "TLS certificate does not cover {Domain}; clients will reject it unless they disable validation",
                    _options.Tls.Domain);
            }

            _certificates.Add(certificate);
            loops.Add(RunListenerAsync(_options.Tls, certificate, _options.Tls.RequireClientCertificate, stoppingToken));
        }

        if (loops.Count == 0)
        {
            _logger.LogWarning("No raw MQTT listeners are enabled.");
            return;
        }

        await Task.WhenAll(loops).ConfigureAwait(false);
    }

    /// <summary>Stops accepting, drains inflight traffic, then closes connections.</summary>
    /// <remarks>先停止 accept 再等待，避免新连接把排空窗口拖长。</remarks>
    /// <param name="cancellationToken">Cancels the shutdown wait.</param>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Stop accepting first so the drain window cannot be extended by new
        // clients, then let inflight QoS traffic settle before closing sockets.
        foreach (var listener in _listeners)
        {
            listener.Stop();
        }

        var drainTimeout = TimeSpan.FromSeconds(Math.Max(0, _options.DrainTimeoutSeconds));

        if (drainTimeout > TimeSpan.Zero)
        {
            var pending = await _broker.DrainAsync(drainTimeout, cancellationToken).ConfigureAwait(false);

            if (pending > 0)
            {
                _logger.LogWarning(
                    "Drain timeout reached with {Pending} session(s) still holding inflight messages",
                    pending);
            }
        }

        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        Task[] pendingConnections;

        lock (_connectionGate)
        {
            pendingConnections = _connections.ToArray();
        }

        if (pendingConnections.Length > 0)
        {
            _logger.LogInformation("Waiting for {Count} MQTT connections to close", pendingConnections.Length);

            try
            {
                await Task.WhenAll(pendingConnections).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Connections are best-effort during shutdown.
            }
        }

        foreach (var bundle in _certificates)
        {
            bundle.Certificate.Dispose();

            if (bundle.Chain is not null)
            {
                foreach (var intermediate in bundle.Chain)
                {
                    intermediate.Dispose();
                }
            }
        }
    }

    /// <summary>Binds one listener and accepts connections until cancellation.</summary>
    /// <remarks>每个连接独立成任务并登记，关闭时据此等待；accept 失败只在取消时静默退出。</remarks>
    /// <param name="listenerOptions">Address and port to bind.</param>
    /// <param name="certificate">Server certificate for TLS, or null for plain TCP.</param>
    /// <param name="requireClientCertificate">True to request a client certificate.</param>
    /// <param name="cancellationToken">Cancels the accept loop.</param>
    private async Task RunListenerAsync(
        TcpListenerOptions listenerOptions,
        MqttCertificateLoader.CertificateBundle? certificate,
        bool requireClientCertificate,
        CancellationToken cancellationToken)
    {
        var address = ResolveAddress(listenerOptions.Host);
        var listener = new TcpListener(address, listenerOptions.Port);
        listener.Start(backlog: 512);

        lock (_connectionGate)
        {
            _listeners.Add(listener);
        }

        _logger.LogInformation(
            "MQTT {Transport} listener started on {Address}:{Port}",
            certificate is null ? "TCP" : "TLS",
            address,
            listenerOptions.Port);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                var task = HandleClientAsync(client, certificate, requireClientCertificate, cancellationToken);

                lock (_connectionGate)
                {
                    _connections.Add(task);
                }

                _ = task.ContinueWith(
                    completed =>
                    {
                        lock (_connectionGate)
                        {
                            _connections.Remove(completed);
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>Wraps one accepted socket in a client connection and runs it.</summary>
    /// <remarks>套接字错误只记 Debug 日志：客户端异常断开是常态，不该刷满 Warning。</remarks>
    /// <param name="client">Accepted TCP client.</param>
    /// <param name="certificate">Server certificate for TLS, or null for plain TCP.</param>
    /// <param name="requireClientCertificate">True to request a client certificate.</param>
    /// <param name="cancellationToken">Cancels the connection loop.</param>
    private async Task HandleClientAsync(
        TcpClient client,
        MqttCertificateLoader.CertificateBundle? certificate,
        bool requireClientCertificate,
        CancellationToken cancellationToken)
    {
        var remote = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
        client.NoDelay = true;

        try
        {
            Stream stream = client.GetStream();
            SslStream? ssl = null;

            if (certificate is not null)
            {
                ssl = new SslStream(stream, leaveInnerStreamOpen: false);
                await ssl.AuthenticateAsServerAsync(
                    new SslServerAuthenticationOptions
                    {
                        ServerCertificateContext = SslStreamCertificateContext.Create(
                            certificate.Certificate,
                            certificate.Chain,
                            offline: false),
                        ClientCertificateRequired = requireClientCertificate,
                        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                    },
                    cancellationToken).ConfigureAwait(false);

                stream = ssl;
            }

            var connection = new ClientConnection(
                stream,
                remote,
                _broker,
                _loggerFactory.CreateLogger<ClientConnection>());

            await connection.RunAsync(cancellationToken).ConfigureAwait(false);

            if (ssl is not null)
            {
                await ssl.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "MQTT connection from {RemoteAddress} ended with an error", remote);
        }
        finally
        {
            client.Dispose();
        }
    }

    /// <summary>Maps a configured host string to a bind address.</summary>
    /// <remarks>只接受 IP 字面量与三个通配写法；写主机名会直接抛，避免 DNS 解析带来的歧义。</remarks>
    /// <param name="host">Configured bind host.</param>
    private static IPAddress ResolveAddress(string host)
    {
        if (string.Equals(host, "0.0.0.0", StringComparison.Ordinal))
        {
            return IPAddress.Any;
        }

        if (string.Equals(host, "::", StringComparison.Ordinal) || string.Equals(host, "*", StringComparison.Ordinal))
        {
            return IPAddress.IPv6Any;
        }

        return IPAddress.TryParse(host, out var address)
            ? address
            : throw new ArgumentException($"Server listener host '{host}' is not a valid IP address.");
    }
}
