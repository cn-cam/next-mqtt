using System.Buffers;
using System.Text;

namespace NextMqtt.Core.Protocol;

/// <summary>Strict UTF-8 helpers for MQTT strings.</summary>
/// <remarks>拒绝非法 UTF-8 与内嵌 U+0000，协议要求而不能放宽。</remarks>
internal static class MqttUtf8
{
    /// <summary>Rejects invalid UTF-8 and never emits a byte order mark.</summary>
    private static readonly UTF8Encoding StrictEncoding = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Decodes UTF-8 and turns an invalid sequence into a protocol error.</summary>
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

    /// <summary>Encodes a string, rejecting characters the protocol forbids.</summary>
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
    /// <summary>Packet body being read; owned by the caller's frame buffer.</summary>
    private readonly ReadOnlySpan<byte> _buffer;
    /// <summary>Position of the next unread byte.</summary>
    private int _offset;

    /// <summary>Wraps a fully framed body; no length prefix is consumed.</summary>
    public MqttSpanReader(ReadOnlySpan<byte> buffer)
    {
        _buffer = buffer;
        _offset = 0;
    }

    /// <summary>Bytes left before the end of the body.</summary>
    public int Remaining => _buffer.Length - _offset;

    /// <summary>True once every byte has been consumed.</summary>
    public bool IsEmpty => _offset >= _buffer.Length;

    /// <summary>Returns the next slice and advances, throwing when the body is short.</summary>
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

    /// <summary>Returns everything left and moves to the end.</summary>
    public ReadOnlySpan<byte> ReadRemaining()
    {
        var slice = _buffer[_offset..];
        _offset = _buffer.Length;
        return slice;
    }

    /// <summary>Reads one byte.</summary>
    public byte ReadByte()
    {
        if (_offset >= _buffer.Length)
        {
            throw MqttProtocolException.Malformed("Truncated packet.");
        }

        return _buffer[_offset++];
    }

    /// <summary>Reads a big-endian two-byte field.</summary>
    public ushort ReadUInt16() => System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(ReadBytes(2));

    /// <summary>Reads a big-endian four-byte field.</summary>
    public uint ReadUInt32() => System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(ReadBytes(4));

    /// <summary>Decodes the one-to-four byte length prefix.</summary>
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

    /// <summary>Reads a length-prefixed UTF-8 string.</summary>
    public string ReadString()
    {
        var length = ReadUInt16();
        return MqttUtf8.Decode(ReadBytes(length));
    }

    /// <summary>Reads a length-prefixed byte field.</summary>
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
    /// <summary>Writes a single byte.</summary>
    public static void WriteByte(IBufferWriter<byte> writer, byte value)
    {
        var span = writer.GetSpan(1);
        span[0] = value;
        writer.Advance(1);
    }

    /// <summary>Writes a big-endian two-byte field.</summary>
    public static void WriteUInt16(IBufferWriter<byte> writer, ushort value)
    {
        var span = writer.GetSpan(2);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian(span, value);
        writer.Advance(2);
    }

    /// <summary>Writes a big-endian four-byte field.</summary>
    public static void WriteUInt32(IBufferWriter<byte> writer, uint value)
    {
        var span = writer.GetSpan(4);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(span, value);
        writer.Advance(4);
    }

    /// <summary>Encodes the one-to-four byte length prefix.</summary>
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

    /// <summary>Writes a length-prefixed byte field.</summary>
    public static void WriteBytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        if (value.IsEmpty)
        {
            return;
        }

        writer.Write(value);
    }

    /// <summary>Writes a length-prefixed UTF-8 string.</summary>
    public static void WriteString(IBufferWriter<byte> writer, string value)
    {
        var bytes = MqttUtf8.Encode(value);
        WriteUInt16(writer, (ushort)bytes.Length);
        WriteBytes(writer, bytes);
    }

    /// <summary>Writes a length-prefixed binary field.</summary>
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
