using System.Buffers;
using System.Text;

namespace NextMqtt.Core.Protocol;

/// <summary>Strict UTF-8 helpers for MQTT strings.</summary>
/// <remarks>拒绝非法 UTF-8 与内嵌 U+0000，协议要求而不能放宽。</remarks>
internal static class MqttUtf8
{
    private static readonly UTF8Encoding StrictEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        foreach (var b in bytes)
        {
            if (b == 0)
            {
                throw MqttProtocolException.Malformed("MQTT strings must not contain U+0000.");
            }
        }

        try
        {
            return StrictEncoding.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            throw MqttProtocolException.Malformed("Invalid UTF-8 string.");
        }
    }

    public static byte[] Encode(string value)
    {
        if (value.Contains('\0'))
        {
            throw MqttProtocolException.Malformed("MQTT strings must not contain U+0000.");
        }

        byte[] bytes;
        try
        {
            bytes = StrictEncoding.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            throw MqttProtocolException.Malformed("String contains an invalid surrogate pair.");
        }

        if (bytes.Length > MqttLimits.MaxFieldLength)
        {
            throw MqttProtocolException.Malformed("String exceeds the MQTT field length limit.");
        }

        return bytes;
    }
}

/// <summary>Forward-only reader over a decoded packet body.</summary>
/// <remarks>只前进不回退；越界读取立即抛异常，避免带病解析。</remarks>
internal ref struct MqttSpanReader
{
    private readonly ReadOnlySpan<byte> _buffer;
    private int _offset;

    public MqttSpanReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _offset = 0;
    }

    public int Remaining => _buffer.Length - _offset;

    public bool IsEmpty => _offset >= _buffer.Length;

    public ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || count > Remaining)
        {
            throw MqttProtocolException.Malformed("Truncated packet.");
        }

        var slice = _buffer.Slice(_offset, count);
        _offset += count;
        return slice;
    }

    public ReadOnlySpan<byte> ReadRemaining()
    {
        var slice = _buffer[_offset..];
        _offset = _buffer.Length;
        return slice;
    }

    public byte ReadByte()
    {
        if (_offset >= _buffer.Length)
        {
            throw MqttProtocolException.Malformed("Truncated packet.");
        }

        return _buffer[_offset++];
    }

    public ushort ReadUInt16() => System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));

    public uint ReadUInt32() => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));

    public int ReadVariableByteInteger()
    {
        var value = 0;
        var multiplier = 1;

        for (var i = 0; i < 4; i++)
        {
            var encoded = ReadByte();
            value += (encoded & 0x7F) * multiplier;

            if ((encoded & 0x80) == 0)
            {
                if (i > 0 && encoded == 0)
                {
                    throw MqttProtocolException.Malformed("Non-canonical variable byte integer.");
                }

                return value;
            }

            multiplier *= 128;
        }

        throw MqttProtocolException.Malformed("Variable byte integer exceeds four bytes.");
    }

    public string ReadString()
    {
        var length = ReadUInt16();
        return MqttUtf8.Decode(ReadBytes(length));
    }

    public byte[] ReadBinary()
    {
        var length = ReadUInt16();
        return ReadBytes(length).ToArray();
    }
}

/// <summary>Big-endian wire writer helpers.</summary>
/// <remarks>所有整数按网络字节序写出，变长整数最多 4 字节。</remarks>
internal static class MqttWire
{
    public static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }

    public static void WriteUInt16(IBufferWriter<byte> writer, ushort value)
    {
        var span = writer.GetSpan(2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(span, value);
        writer.Advance(2);
    }

    public static void WriteUInt32(IBufferWriter<byte> writer, uint value)
    {
        var span = writer.GetSpan(4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(span, value);
        writer.Advance(4);
    }

    public static void WriteVariableByteInteger(IBufferWriter<byte> writer, int value)
    {
        if (value is < 0 or > MqttLimits.MaxRemainingLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Variable byte integer out of range.");
        }

        var span = writer.GetSpan(4);
        var offset = 0;

        do
        {
            var encoded = (byte)(value % 128);
            value /= 128;
            if (value > 0)
            {
                encoded |= 0x80;
            }

            span[offset++] = encoded;
        }
        while (value > 0);

        writer.Advance(offset);
    }

    public static void WriteBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        writer.Write(value);
    }

    public static void WriteString(IBufferWriter<byte> writer, string value)
    {
        var bytes = MqttUtf8.Encode(value);
        WriteUInt16(writer, (ushort)bytes.Length);
        WriteBytes(writer, bytes);
    }

    public static void WriteBinary(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        if (value.Length > MqttLimits.MaxFieldLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Binary field exceeds 65535 bytes.");
        }

        WriteUInt16(writer, (ushort)value.Length);
        WriteBytes(writer, value);
    }
}
