using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Server.Tests;

/// <summary>Minimal MQTT client built on the shared codec, used by integration tests.</summary>
internal sealed class MqttTestClient : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly NetworkStream _stream;
    private readonly MqttPacketReader _reader;
    private readonly MqttProtocolVersion _version;
    private ushort _nextPacketId = 1;

    private MqttTestClient(TcpClient tcp, NetworkStream stream, MqttProtocolVersion version)
    {
        _tcp = tcp;
        _stream = stream;
        _version = version;
        _reader = new MqttPacketReader(PipeReader.Create(stream));
    }

    public ConnAckPacket ConnAck { get; private set; } = new();

    public static async Task<MqttTestClient> ConnectAsync(
        int port,
        ConnectPacket connect,
        CancellationToken cancellationToken = default)
    {
        var tcp = new TcpClient { NoDelay = true };
        await tcp.ConnectAsync(IPAddress.Loopback, port, cancellationToken);
        var client = new MqttTestClient(tcp, tcp.GetStream(), connect.ProtocolVersion);
        await client.SendAsync(connect, cancellationToken);
        client.ConnAck = await client.ReadAsync<ConnAckPacket>(cancellationToken);
        return client;
    }

    public ushort NextPacketId()
    {
        var id = _nextPacketId++;
        return id == 0 ? _nextPacketId++ : id;
    }

    public async Task SendAsync(MqttPacket packet, CancellationToken cancellationToken = default)
    {
        var buffer = new ArrayBufferWriter<byte>(128);
        MqttPacketWriter.Write(buffer, packet, _version);
        await _stream.WriteAsync(buffer.WrittenMemory, cancellationToken);
        await _stream.FlushAsync(cancellationToken);
    }

    public async Task<MqttPacket> ReadPacketAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(5));

        var packet = await _reader.ReadAsync(_version, timeoutSource.Token);

        return packet ?? throw new IOException("Connection closed before a packet was received.");
    }

    public async Task<T> ReadAsync<T>(CancellationToken cancellationToken = default)
        where T : MqttPacket
    {
        var packet = await ReadPacketAsync(cancellationToken: cancellationToken);

        return packet as T
            ?? throw new InvalidOperationException($"Expected {typeof(T).Name}, received {packet.GetType().Name}.");
    }

    public async Task<T> ReadUntilAsync<T>(CancellationToken cancellationToken = default)
        where T : MqttPacket
    {
        while (true)
        {
            var packet = await ReadPacketAsync(cancellationToken: cancellationToken);

            if (packet is T typed)
            {
                return typed;
            }
        }
    }

    public async Task<SubAckPacket> SubscribeAsync(
        string filter,
        MqttQoS qos = MqttQoS.AtMostOnce,
        MqttSubscriptionOptions? options = null,
        int? identifier = null,
        CancellationToken cancellationToken = default)
    {
        var packetId = NextPacketId();
        var properties = new MqttProperties();

        if (identifier is { } subscriptionIdentifier)
        {
            properties.AddVariableByteInteger(MqttPropertyId.SubscriptionIdentifier, subscriptionIdentifier);
        }

        await SendAsync(new SubscribePacket
        {
            PacketId = packetId,
            Properties = properties,
            Subscriptions = [new MqttSubscriptionRequest(filter, options ?? new MqttSubscriptionOptions(qos))],
        }, cancellationToken);

        while (true)
        {
            var packet = await ReadPacketAsync(cancellationToken: cancellationToken);

            if (packet is SubAckPacket subAck)
            {
                return subAck;
            }

            throw new InvalidOperationException($"Received {packet.Type} before SUBACK.");
        }
    }

    public async Task<ushort> PublishAsync(
        string topic,
        string payload,
        MqttQoS qos = MqttQoS.AtMostOnce,
        bool retain = false,
        CancellationToken cancellationToken = default)
    {
        var packetId = qos == MqttQoS.AtMostOnce ? (ushort)0 : NextPacketId();

        await SendAsync(new PublishPacket
        {
            Topic = topic,
            Payload = System.Text.Encoding.UTF8.GetBytes(payload),
            QoS = qos,
            Retain = retain,
            PacketId = packetId,
        }, cancellationToken);

        return packetId;
    }

    public async Task DisconnectAsync(byte reasonCode = MqttReasonCodes.Success, CancellationToken cancellationToken = default)
    {
        if (_version == MqttProtocolVersion.V500)
        {
            await SendAsync(new DisconnectPacket { ReasonCode = reasonCode }, cancellationToken);
        }
        else
        {
            await SendAsync(new DisconnectPacket(), cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _stream.Dispose();
            _tcp.Dispose();
        }
        catch (Exception)
        {
            // Ignore socket teardown errors in tests.
        }

        await Task.CompletedTask;
    }
}
