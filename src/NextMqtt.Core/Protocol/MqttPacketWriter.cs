using System.Buffers;

namespace NextMqtt.Core.Protocol;

/// <summary>Encodes MQTT control packets.</summary>
/// <remarks>按连接协商的版本编码，3.1.1 不写 MQTT 5 属性。</remarks>
public static class MqttPacketWriter
{
    /// <summary>Serializes one packet, writing the fixed and variable headers.</summary>
    /// <remarks>先写剩余长度占位再回填，保证长度与内容一致；3.1.1 不编码 MQTT 5 属性。</remarks>
    /// <param name="writer">Destination buffer; the caller owns flushing.</param>
    /// <param name="packet">Packet to encode.</param>
    /// <param name="version">Protocol level of the target connection.</param>
    public static void Write(IBufferWriter<byte> writer, MqttPacket packet, MqttProtocolVersion version)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(packet);

        var body = new ArrayBufferWriter<byte>(128);
        byte flags = 0;

        switch (packet)
        {
            case ConnectPacket connect:
                WriteConnect(body, connect);
                break;
            case ConnAckPacket connAck:
                WriteConnAck(body, connAck, version);
                break;
            case PublishPacket publish:
                flags = (byte)((publish.Duplicate ? 0x08 : 0) | ((byte)publish.QoS << 1) | (publish.Retain ? 0x01 : 0));
                WritePublish(body, publish, version);
                break;
            case PubAckPacket pubAck:
                WriteAck(body, pubAck, version);
                break;
            case PubRecPacket pubRec:
                WriteAck(body, pubRec, version);
                break;
            case PubRelPacket pubRel:
                flags = 0x02;
                WriteAck(body, pubRel, version);
                break;
            case PubCompPacket pubComp:
                WriteAck(body, pubComp, version);
                break;
            case SubscribePacket subscribe:
                flags = 0x02;
                WriteSubscribe(body, subscribe, version);
                break;
            case SubAckPacket subAck:
                WriteSubAck(body, subAck, version);
                break;
            case UnsubscribePacket unsubscribe:
                flags = 0x02;
                WriteUnsubscribe(body, unsubscribe, version);
                break;
            case UnsubAckPacket unsubAck:
                WriteUnsubAck(body, unsubAck, version);
                break;
            case PingReqPacket or PingRespPacket:
                break;
            case DisconnectPacket disconnect:
                WriteDisconnect(body, disconnect, version);
                break;
            case AuthPacket auth:
                if (version != MqttProtocolVersion.V500)
                {
                    throw new InvalidOperationException("AUTH requires MQTT 5.");
                }

                MqttPropertyValidation.Validate(MqttPacketType.Auth, auth.Properties, version);
                MqttWire.WriteByte(body, auth.ReasonCode);
                MqttProperties.Write(body, auth.Properties);
                break;
            default:
                throw new InvalidOperationException($"Unsupported packet type {packet.GetType().Name}.");
        }

        if (body.WrittenCount > MqttLimits.MaxRemainingLength)
        {
            throw MqttProtocolException.TooLarge("Packet exceeds the MQTT maximum remaining length.");
        }

        MqttWire.WriteByte(writer, (byte)(((byte)packet.Type << 4) | flags));
        MqttWire.WriteVariableByteInteger(writer, body.WrittenCount);
        MqttWire.WriteBytes(writer, body.WrittenSpan);
    }

    /// <summary>Writes the protocol name, connect flags and CONNECT properties.</summary>
    private static void WriteConnect(IBufferWriter<byte> body, ConnectPacket packet)
    {
        MqttWire.WriteString(body, "MQTT");
        MqttWire.WriteByte(body, (byte)packet.ProtocolVersion);

        var flags = 0;
        if (packet.Username is not null)
        {
            flags |= 0x80;
        }

        if (packet.Password is not null)
        {
            flags |= 0x40;
        }

        if (packet.Will is not null)
        {
            flags |= 0x04;
            flags |= (byte)((byte)packet.Will.QoS << 3);
            if (packet.Will.Retain)
            {
                flags |= 0x20;
            }
        }

        if (packet.CleanStart)
        {
            flags |= 0x02;
        }

        MqttWire.WriteByte(body, (byte)flags);
        MqttWire.WriteUInt16(body, packet.KeepAliveSeconds);

        MqttPropertyValidation.Validate(MqttPacketType.Connect, packet.Properties, packet.ProtocolVersion);
        if (packet.ProtocolVersion == MqttProtocolVersion.V500)
        {
            MqttProperties.Write(body, packet.Properties);
        }
        else if (!packet.Properties.IsEmpty)
        {
            throw new InvalidOperationException("MQTT 3.1.1 CONNECT cannot carry properties.");
        }

        MqttWire.WriteString(body, packet.ClientId);

        if (packet.Will is not null)
        {
            if (packet.ProtocolVersion == MqttProtocolVersion.V500)
            {
                MqttProperties.Write(body, packet.Will.Properties);
            }
            else if (!packet.Will.Properties.IsEmpty)
            {
                throw new InvalidOperationException("MQTT 3.1.1 wills cannot carry properties.");
            }

            MqttWire.WriteString(body, packet.Will.Topic);
            MqttWire.WriteBinary(body, packet.Will.Payload);
        }

        if (packet.Username is not null)
        {
            MqttWire.WriteString(body, packet.Username);
        }

        if (packet.Password is not null)
        {
            MqttWire.WriteBinary(body, packet.Password);
        }
    }

    /// <summary>Writes CONNACK, omitting the 5.0 fields for 3.1.1 peers.</summary>
    private static void WriteConnAck(IBufferWriter<byte> body, ConnAckPacket packet, MqttProtocolVersion version)
    {
        MqttWire.WriteByte(body, packet.SessionPresent ? (byte)0x01 : (byte)0x00);
        MqttWire.WriteByte(body, packet.ReasonCode);

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.ConnAck, packet.Properties, version);
            MqttProperties.Write(body, packet.Properties);
        }
        else if (!packet.Properties.IsEmpty)
        {
            throw new InvalidOperationException("MQTT 3.1.1 CONNACK cannot carry properties.");
        }
    }

    /// <summary>Writes PUBLISH, including the packet identifier only above QoS 0.</summary>
    private static void WritePublish(IBufferWriter<byte> body, PublishPacket packet, MqttProtocolVersion version)
    {
        if (packet.QoS != MqttQoS.AtMostOnce && packet.PacketId == 0)
        {
            throw new InvalidOperationException("QoS 1 and 2 PUBLISH packets require a packet identifier.");
        }

        if (packet.QoS == MqttQoS.AtMostOnce && packet.Duplicate)
        {
            throw new InvalidOperationException("QoS 0 PUBLISH packets cannot set the duplicate flag.");
        }

        MqttWire.WriteString(body, packet.Topic);

        if (packet.QoS != MqttQoS.AtMostOnce)
        {
            MqttWire.WriteUInt16(body, packet.PacketId);
        }

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.Publish, packet.Properties, version);
            MqttProperties.Write(body, packet.Properties);
        }
        else if (!packet.Properties.IsEmpty)
        {
            throw new InvalidOperationException("MQTT 3.1.1 PUBLISH cannot carry properties.");
        }

        MqttWire.WriteBytes(body, packet.Payload);
    }

    /// <summary>Writes PUBACK, PUBREC, PUBREL or PUBCOMP with an optional reason code.</summary>
    private static void WriteAck(IBufferWriter<byte> body, MqttAcknowledgementPacket packet, MqttProtocolVersion version)
    {
        if (packet.PacketId == 0)
        {
            throw new InvalidOperationException("Acknowledgement packet identifier must not be zero.");
        }

        MqttWire.WriteUInt16(body, packet.PacketId);

        if (version == MqttProtocolVersion.V311)
        {
            if (packet.ReasonCode != MqttReasonCodes.Success || !packet.Properties.IsEmpty)
            {
                throw new InvalidOperationException("MQTT 3.1.1 acknowledgements cannot carry a reason code or properties.");
            }

            return;
        }

        MqttPropertyValidation.Validate(packet.Type, packet.Properties, version);

        if (packet.ReasonCode == MqttReasonCodes.Success && packet.Properties.IsEmpty)
        {
            return;
        }

        MqttWire.WriteByte(body, packet.ReasonCode);
        MqttProperties.Write(body, packet.Properties);
    }

    /// <summary>Writes SUBSCRIBE with one options byte per filter.</summary>
    private static void WriteSubscribe(IBufferWriter<byte> body, SubscribePacket packet, MqttProtocolVersion version)
    {
        if (packet.PacketId == 0)
        {
            throw new InvalidOperationException("SUBSCRIBE packet identifier must not be zero.");
        }

        if (packet.Subscriptions.Count == 0)
        {
            throw new InvalidOperationException("SUBSCRIBE must contain at least one subscription.");
        }

        MqttWire.WriteUInt16(body, packet.PacketId);

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.Subscribe, packet.Properties, version);
            MqttProperties.Write(body, packet.Properties);
        }
        else if (!packet.Properties.IsEmpty)
        {
            throw new InvalidOperationException("MQTT 3.1.1 SUBSCRIBE cannot carry properties.");
        }

        foreach (var subscription in packet.Subscriptions)
        {
            MqttWire.WriteString(body, subscription.Filter);
            MqttWire.WriteByte(body, subscription.Options.Encode());
        }
    }

    /// <summary>Writes SUBACK with one reason code per filter.</summary>
    private static void WriteSubAck(IBufferWriter<byte> body, SubAckPacket packet, MqttProtocolVersion version)
    {
        if (packet.PacketId == 0 || packet.ReasonCodes.Count == 0)
        {
            throw new InvalidOperationException("SUBACK requires a packet identifier and at least one reason code.");
        }

        MqttWire.WriteUInt16(body, packet.PacketId);

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.SubAck, packet.Properties, version);
            MqttProperties.Write(body, packet.Properties);
        }

        foreach (var code in packet.ReasonCodes)
        {
            MqttWire.WriteByte(body, code);
        }
    }

    /// <summary>Writes UNSUBSCRIBE filters.</summary>
    private static void WriteUnsubscribe(IBufferWriter<byte> body, UnsubscribePacket packet, MqttProtocolVersion version)
    {
        if (packet.PacketId == 0 || packet.Filters.Count == 0)
        {
            throw new InvalidOperationException("UNSUBSCRIBE requires a packet identifier and at least one filter.");
        }

        MqttWire.WriteUInt16(body, packet.PacketId);

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.Unsubscribe, packet.Properties, version);
            MqttProperties.Write(body, packet.Properties);
        }
        else if (!packet.Properties.IsEmpty)
        {
            throw new InvalidOperationException("MQTT 3.1.1 UNSUBSCRIBE cannot carry properties.");
        }

        foreach (var filter in packet.Filters)
        {
            MqttWire.WriteString(body, filter);
        }
    }

    /// <summary>Writes UNSUBACK, collapsing to a bare packet for 3.1.1.</summary>
    private static void WriteUnsubAck(IBufferWriter<byte> body, UnsubAckPacket packet, MqttProtocolVersion version)
    {
        if (packet.PacketId == 0)
        {
            throw new InvalidOperationException("UNSUBACK requires a packet identifier.");
        }

        MqttWire.WriteUInt16(body, packet.PacketId);

        if (version == MqttProtocolVersion.V311)
        {
            return;
        }

        if (packet.ReasonCodes.Count == 0)
        {
            throw new InvalidOperationException("MQTT 5 UNSUBACK requires at least one reason code.");
        }

        MqttPropertyValidation.Validate(MqttPacketType.UnsubAck, packet.Properties, version);
        MqttProperties.Write(body, packet.Properties);
        foreach (var code in packet.ReasonCodes)
        {
            MqttWire.WriteByte(body, code);
        }
    }

    /// <summary>Writes DISCONNECT, adding a reason code only for 5.0 peers.</summary>
    private static void WriteDisconnect(IBufferWriter<byte> body, DisconnectPacket packet, MqttProtocolVersion version)
    {
        if (version == MqttProtocolVersion.V311)
        {
            if (packet.ReasonCode != MqttReasonCodes.Success || !packet.Properties.IsEmpty)
            {
                throw new InvalidOperationException("MQTT 3.1.1 DISCONNECT has no reason code or properties.");
            }

            return;
        }

        MqttPropertyValidation.Validate(MqttPacketType.Disconnect, packet.Properties, version);

        if (packet.ReasonCode == MqttReasonCodes.Success && packet.Properties.IsEmpty)
        {
            return;
        }

        MqttWire.WriteByte(body, packet.ReasonCode);
        MqttProperties.Write(body, packet.Properties);
    }
}
