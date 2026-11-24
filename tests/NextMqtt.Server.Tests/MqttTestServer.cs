using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker;
using NextMqtt.Broker.Clustering;
using NextMqtt.Broker.Connections;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Server.Tests;

/// <summary>Runs a real broker on a loopback TCP port for integration tests.</summary>
internal sealed class MqttTestServer : IAsyncDisposable
{
    private readonly MqttBroker _broker;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _connections = [];
    private readonly Task _acceptTask;

    private MqttTestServer(MqttBroker broker, TcpListener listener)
    {
        _broker = broker;
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _acceptTask = AcceptLoopAsync();
    }

    public int Port { get; }

    public MqttBroker Broker => _broker;

    public static MqttTestServer Start(
        AuthenticationOptions? authentication = null,
        AclOptions? acl = null,
        Action<BrokerOptions>? configure = null)
    {
        var options = new BrokerOptions
        {
            SweepInterval = TimeSpan.FromSeconds(1),
            RetransmitInterval = TimeSpan.FromSeconds(1),
        };

        configure?.Invoke(options);

        var broker = new MqttBroker(
            options,
            new ClientAuthenticator(authentication ?? new AuthenticationOptions { AllowAnonymous = true }),
            new TopicAuthorizer(acl ?? new AclOptions { DefaultPolicy = AclDefaultPolicy.Allow }),
            NullClusterRouter.Instance,
            NullLogger<MqttBroker>.Instance);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(backlog: 64);

        return new MqttTestServer(broker, listener);
    }

    public async Task<MqttTestClient> ConnectAsync(
        ConnectPacket connect,
        CancellationToken cancellationToken = default)
        => await MqttTestClient.ConnectAsync(Port, connect, cancellationToken);

    public async Task<bool> WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _listener.Stop();

        try
        {
            await _acceptTask.WaitAsync(TimeSpan.FromSeconds(2));
        }
        catch (Exception)
        {
            // Expected during teardown.
        }

        foreach (var connection in _connections.ToArray())
        {
            try
            {
                await connection.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch (Exception)
            {
                // Expected during teardown.
            }
        }

        await _broker.DisposeAsync();
        _cts.Dispose();
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;

                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (SocketException) when (_cts.IsCancellationRequested)
                {
                    break;
                }

                var task = HandleAsync(client);
                _connections.Add(task);
            }
        }
        catch (Exception)
        {
            // Listener stopped.
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        client.NoDelay = true;

        try
        {
            var connection = new ClientConnection(
                client.GetStream(),
                client.Client.RemoteEndPoint?.ToString() ?? "test",
                _broker,
                NullLogger<ClientConnection>.Instance);

            await connection.RunAsync(_cts.Token);
        }
        catch (Exception)
        {
            // Connection closed.
        }
        finally
        {
            client.Dispose();
        }
    }
}
