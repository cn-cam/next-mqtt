using System.Buffers;
using System.IO.Pipelines;

namespace NextMqtt.Core.Protocol;

/// <summary>
/// Reads complete MQTT frames from a <see cref="PipeReader"/>. A single
/// instance is not thread-safe; use it from one connection loop.
/// </summary>
public sealed class MqttPacketReader
{
    private readonly PipeReader _reader;
    private readonly int _maxPacketSize;

    public MqttPacketReader(PipeReader reader, int maxPacketSize = MqttLimits.DefaultMaxPacketSize)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPacketSize, 2);

        _reader = reader;
        _maxPacketSize = maxPacketSize;
    }

    public async ValueTask<MqttPacket?> ReadAsync(
        MqttProtocolVersion version,
        CancellationToken cancellationToken = default)
    {
        while (true)
        {
            var result = await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);
            var buffer = result.Buffer;

            if (buffer.IsEmpty)
            {
                _reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted)
                {
                    return null;
                }

                continue;
            }

            var sequence = new SequenceReader<byte>(buffer);

            if (!sequence.TryRead(out var header))
            {
                AdvanceForMoreData(buffer);
                continue;
            }

            var packetType = (MqttPacketType)(header >> 4);
            if (packetType is < MqttPacketType.Connect or > MqttPacketType.Auth)
            {
                throw MqttProtocolException.Malformed($"Unknown packet type {(int)packetType}.");
            }

            var remainingLength = 0;
            var multiplier = 1;
            var complete = false;

            for (var i = 0; i < 4; i++)
            {
                if (!sequence.TryRead(out var encoded))
                {
                    break;
                }

                remainingLength += (encoded & 0x7F) * multiplier;

                if ((encoded & 0x80) == 0)
                {
                    if (i > 0 && encoded == 0)
                    {
                        throw MqttProtocolException.Malformed("Non-canonical remaining length.");
                    }

                    complete = true;
                    break;
                }

                multiplier *= 128;

                if (i == 3)
                {
                    throw MqttProtocolException.Malformed("Remaining length exceeds four bytes.");
                }
            }

            if (!complete)
            {
                AdvanceForMoreData(buffer);
                if (result.IsCompleted)
                {
                    throw MqttProtocolException.Malformed("Connection closed inside a packet header.");
                }

                continue;
            }

            var totalLength = sequence.Consumed + remainingLength;

            if (totalLength > _maxPacketSize)
            {
                throw MqttProtocolException.TooLarge(
                    $"Packet of {totalLength} bytes exceeds the {_maxPacketSize} byte limit.");
            }

            if (buffer.Length < totalLength)
            {
                AdvanceForMoreData(buffer);
                if (result.IsCompleted)
                {
                    throw MqttProtocolException.Malformed("Connection closed inside a packet body.");
                }

                continue;
            }

            var body = buffer.Slice(sequence.Position, remainingLength).ToArray();
            _reader.AdvanceTo(buffer.GetPosition(totalLength));

            return MqttPacketDecoder.Decode(body, header, version);
        }
    }

    private void AdvanceForMoreData(ReadOnlySequence<byte> buffer)
        => _reader.AdvanceTo(buffer.Start, buffer.End);
}
