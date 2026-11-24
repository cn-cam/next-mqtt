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
public static class MqttCertificateLoader
{
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

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken).ConfigureAwait(false);

        foreach (var listener in _listeners)
        {
            listener.Stop();
        }

        Task[] pending;

        lock (_connectionGate)
        {
            pending = _connections.ToArray();
        }

        if (pending.Length > 0)
        {
            _logger.LogInformation("Waiting for {Count} MQTT connections to close", pending.Length);

            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
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
