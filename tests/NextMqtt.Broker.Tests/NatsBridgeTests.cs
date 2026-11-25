using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker.Integrations;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

public sealed class NatsBridgeTests
{
    /// <summary>Minimal NATS server: greets, ignores CONNECT and records PUB frames.</summary>
    private sealed class FakeNatsServer : IAsyncDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _loop;
        private readonly List<(string Subject, string Payload)> _messages = [];

        public FakeNatsServer()
        {
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _loop = Task.Run(AcceptAsync);
        }

        public int Port { get; }

        public List<(string Subject, string Payload)> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();

            try
            {
                await _loop;
            }
            catch (Exception)
            {
                // Listener stopped.
            }

            _cts.Dispose();
        }

        private async Task AcceptAsync()
        {
            using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
            var stream = client.GetStream();
            await stream.WriteAsync("INFO {\"server_id\":\"test\"}\r\n"u8.ToArray(), _cts.Token);

            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);

                if (line is null)
                {
                    return;
                }

                if (!line.StartsWith("PUB ", StringComparison.Ordinal))
                {
                    continue;
                }

                var parts = line.Split(' ');
                var length = int.Parse(parts[2]);
                var buffer = new char[length];
                var read = 0;

                while (read < length)
                {
                    read += await reader.ReadAsync(buffer.AsMemory(read, length - read), _cts.Token);
                }

                // Consume the trailing CRLF.
                await reader.ReadLineAsync(_cts.Token);

                lock (_messages)
                {
                    _messages.Add((parts[1], new string(buffer)));
                }
            }
        }
    }

    [Fact]
    public async Task Matching_publish_is_forwarded_with_a_mapped_subject()
    {
        await using var server = new FakeNatsServer();
        await using var bridge = new NatsBridge(
            new NatsOptions
            {
                Enabled = true,
                Url = $"nats://127.0.0.1:{server.Port}",
                SubjectPrefix = "mqtt.",
                Filter = "sensors/#",
            },
            NullLogger<NatsBridge>.Instance);

        bridge.Publish(new PublishPacket
        {
            Topic = "sensors/room 1/temp",
            Payload = "21.5"u8.ToArray(),
            QoS = MqttQoS.AtMostOnce,
        });

        bridge.Publish(new PublishPacket
        {
            Topic = "other/topic",
            Payload = "x"u8.ToArray(),
        });

        for (var attempt = 0; attempt < 100 && bridge.Forwarded == 0; attempt++)
        {
            await Task.Delay(50);
        }

        var message = Assert.Single(server.Messages);
        Assert.Equal("mqtt.sensors.room_1.temp", message.Subject);
        Assert.Equal("21.5", message.Payload);
        Assert.Equal(1, bridge.Forwarded);
    }
}
