using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using NextMqtt.Broker;
using NextMqtt.Broker.Connections;
using NextMqtt.Server.Options;
using Microsoft.Extensions.Logging;

namespace NextMqtt.Server.Networking;

/// <summary>Loads PFX/PKCS#12 or PEM certificates for MQTT/TLS listeners.</summary>
/// <remarks>PEM 优先找同目录同名的 .key；PFX 用临时密钥集，避免在容器里写入用户配置目录。</remarks>
public static class MqttCertificateLoader
{
    /// <summary>Loads a PFX/PEM certificate from disk.</summary>
    /// <remarks>文件不存在直接抛，属于启动配置错误；PEM 的私钥必须与证书同名。</remarks>
    /// <param name="path">Certificate file path.</param>
    /// <param name="password">PFX password, when required.</param>
    public static X509Certificate2 Load(string path, string? password)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Certificate file was not found.", path);
        }

        if (path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
        {
            var keyPath = Path.ChangeExtension(path, ".key");

            return File.Exists(keyPath)
                ? X509Certificate2.CreateFromPemFile(path, keyPath)
                : X509Certificate2.CreateFromPemFile(path);
        }

        return X509CertificateLoader.LoadPkcs12FromFile(path, password, X509KeyStorageFlags.EphemeralKeySet);
    }
}

/// <summary>Accepts MQTT connections on raw TCP and TLS sockets.</summary>
/// <remarks>每个启用的监听器一条 accept 循环；关闭时先停止接收，再等待 inflight 确认。</remarks>
public sealed class MqttTcpListenerService : BackgroundService
{
    private readonly MqttBroker _broker;
    private readonly ServerOptions _options;
    private readonly ILogger<MqttTcpListenerService> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly List<TcpListener> _listeners = [];
    private readonly List<X509Certificate2> _certificates = [];
    private readonly List<Task> _connections = [];
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

            var certificate = MqttCertificateLoader.Load(_options.Tls.CertificatePath, _options.Tls.CertificatePassword);
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

        foreach (var certificate in _certificates)
        {
            certificate.Dispose();
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
        X509Certificate2? certificate,
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
        X509Certificate2? certificate,
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
                        ServerCertificate = certificate,
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
