using System.Buffers;

namespace NextMqtt.Core.Protocol;

/// <summary>Decodes a packet body (without the fixed header) into a model.</summary>
/// <remarks>定长头已由 Reader 剥掉；这里只管变长头与负载，越界按 0x81 拒绝。</remarks>
internal static class MqttPacketDecoder
{
    /// <summary>Dispatches on the packet type nibble and rejects unknown types.</summary>
    public static MqttPacket Decode(ReadOnlySpan<byte> body, byte header, MqttProtocolVersion version)
    {
        var packetType = (MqttPacketType)(header >> 4);
        var flags = (byte)(header & 0x0F);
        var reader = new MqttSpanReader(body);

        return packetType switch
        {
            MqttPacketType.Connect => DecodeConnect(ref reader, flags),
            MqttPacketType.ConnAck => DecodeConnAck(ref reader, flags, version),
            MqttPacketType.Publish => DecodePublish(ref reader, flags, version),
            MqttPacketType.PubAck => DecodeAck<PubAckPacket>(ref reader, flags, version),
            MqttPacketType.PubRec => DecodeAck<PubRecPacket>(ref reader, flags, version),
            MqttPacketType.PubRel => DecodeAck<PubRelPacket>(ref reader, flags, version, requiredFlags: 0x02),
            MqttPacketType.PubComp => DecodeAck<PubCompPacket>(ref reader, flags, version),
            MqttPacketType.Subscribe => DecodeSubscribe(ref reader, flags, version),
            MqttPacketType.SubAck => DecodeSubAck(ref reader, flags, version),
            MqttPacketType.Unsubscribe => DecodeUnsubscribe(ref reader, flags, version),
            MqttPacketType.UnsubAck => DecodeUnsubAck(ref reader, flags, version),
            MqttPacketType.PingReq => DecodeEmpty<PingReqPacket>(ref reader, flags),
            MqttPacketType.PingResp => DecodeEmpty<PingRespPacket>(ref reader, flags),
            MqttPacketType.Disconnect => DecodeDisconnect(ref reader, flags, version),
            MqttPacketType.Auth => DecodeAuth(ref reader, flags, version),
            _ => throw MqttProtocolException.Malformed($"Unknown packet type {(int)packetType}."),
        };
    }

    /// <summary>Reads CONNECT in wire order, validating the protocol name and reserved flags.</summary>
    private static ConnectPacket DecodeConnect(ref MqttSpanReader reader, byte flags)
    {
        if (flags != 0)
        {
            throw MqttProtocolException.Malformed("CONNECT reserved header flags must be zero.");
        }

        var protocolName = reader.ReadString();
        var protocolLevel = reader.ReadByte();

        if (protocolName != "MQTT" || protocolLevel is not (4 or 5))
        {
            throw new MqttProtocolException(
                MqttReasonCodes.UnsupportedProtocolVersion,
                $"Unsupported protocol '{protocolName}' level {protocolLevel}.");
        }

        var version = (MqttProtocolVersion)protocolLevel;
        var connectFlags = reader.ReadByte();

        if ((connectFlags & 0x01) != 0)
        {
            throw MqttProtocolException.Malformed("CONNECT reserved flag must be zero.");
        }

        var cleanStart = (connectFlags & 0x02) != 0;
        var willFlag = (connectFlags & 0x04) != 0;
        var willQoS = (MqttQoS)((connectFlags >> 3) & 0x03);
        var willRetain = (connectFlags & 0x20) != 0;
        var passwordFlag = (connectFlags & 0x40) != 0;
        var usernameFlag = (connectFlags & 0x80) != 0;

        if (willQoS == (MqttQoS)3)
        {
            throw MqttProtocolException.Malformed("CONNECT will QoS 3 is invalid.");
        }

        if (!willFlag && (willQoS != 0 || willRetain))
        {
            throw MqttProtocolException.Malformed("CONNECT will flags set without a will message.");
        }

        if (passwordFlag && !usernameFlag)
        {
            throw MqttProtocolException.Malformed("CONNECT password flag requires the username flag.");
        }

        var keepAlive = reader.ReadUInt16();
        var properties = version == MqttProtocolVersion.V500 ? MqttProperties.Read(ref reader) : new MqttProperties();

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.Connect, properties, version);
        }

        var clientId = reader.ReadString();
        MqttWill? will = null;

        if (willFlag)
        {
            var willProperties = version == MqttProtocolVersion.V500 ? MqttProperties.Read(ref reader) : new MqttProperties();
            if (version == MqttProtocolVersion.V500)
            {
                foreach (var property in willProperties.Items)
                {
                    if (property.Id is not (MqttPropertyId.WillDelayInterval
                        or MqttPropertyId.PayloadFormatIndicator
                        or MqttPropertyId.MessageExpiryInterval
                        or MqttPropertyId.ContentType
                        or MqttPropertyId.ResponseTopic
                        or MqttPropertyId.CorrelationData
                        or MqttPropertyId.UserProperty))
                    {
                        throw MqttProtocolException.Protocol($"Property {property.Id} is not allowed on a will message.");
                    }
                }
            }

            var willTopic = reader.ReadString();
            var willPayload = reader.ReadBinary();

            will = new MqttWill
            {
                Topic = willTopic,
                Payload = willPayload,
                QoS = willQoS,
                Retain = willRetain,
                Properties = willProperties,
            };
        }

        var username = usernameFlag ? reader.ReadString() : null;
        var password = passwordFlag ? reader.ReadBinary() : null;

        if (!reader.IsEmpty)
        {
            throw MqttProtocolException.Malformed("CONNECT contains trailing bytes.");
        }

        return new ConnectPacket
        {
            ProtocolVersion = version,
            CleanStart = cleanStart,
            KeepAliveSeconds = keepAlive,
            ClientId = clientId,
            Properties = properties,
            Will = will,
            Username = username,
            Password = password,
        };
    }

    /// <summary>Reads CONNACK and maps 3.1.1 return codes onto 5.0 reason codes.</summary>
    private static ConnAckPacket DecodeConnAck(ref MqttSpanReader reader, byte flags, MqttProtocolVersion version)
    {
        if (flags != 0)
        {
            throw MqttProtocolException.Malformed("CONNACK reserved header flags must be zero.");
        }

        var ackFlags = reader.ReadByte();
        if ((ackFlags & 0xFE) != 0)
        {
            throw MqttProtocolException.Malformed("CONNACK acknowledge flags contain reserved bits.");
        }

        var reasonCode = reader.ReadByte();
        var properties = version == MqttProtocolVersion.V500 ? MqttProperties.Read(ref reader) : new MqttProperties();

        if (!reader.IsEmpty)
        {
            throw MqttProtocolException.Malformed("CONNACK contains trailing bytes.");
        }

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.ConnAck, properties, version);
        }

        return new ConnAckPacket
        {
            SessionPresent = (ackFlags & 0x01) != 0,
            ReasonCode = reasonCode,
            Properties = properties,
        };
    }

    /// <summary>Reads PUBLISH; the topic may be empty when the client sends only an alias.</summary>
    private static PublishPacket DecodePublish(ref MqttSpanReader reader, byte flags, MqttProtocolVersion version)
    {
        var duplicate = (flags & 0x08) != 0;
        var qos = (MqttQoS)((flags >> 1) & 0x03);
        var retain = (flags & 0x01) != 0;

        if (qos == (MqttQoS)3)
        {
            throw MqttProtocolException.Malformed("PUBLISH QoS 3 is invalid.");
        }

        if (qos == MqttQoS.AtMostOnce && duplicate)
        {
            throw MqttProtocolException.Malformed("PUBLISH with QoS 0 must not set the duplicate flag.");
        }

        var topic = reader.ReadString();
        var packetId = qos == MqttQoS.AtMostOnce ? (ushort)0 : reader.ReadUInt16();

        if (qos != MqttQoS.AtMostOnce && packetId == 0)
        {
            throw MqttProtocolException.Protocol("PUBLISH packet identifier must not be zero.");
        }

        var properties = version == MqttProtocolVersion.V500 ? MqttProperties.Read(ref reader) : new MqttProperties();

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.Publish, properties, version);
        }

        return new PublishPacket
        {
            Topic = topic,
            Payload = reader.ReadRemaining().ToArray(),
            QoS = qos,
            Retain = retain,
            Duplicate = duplicate,
            PacketId = packetId,
            Properties = properties,
        };
    }

    /// <summary>Reads PUBACK, PUBREC, PUBREL or PUBCOMP; 3.1.1 carries no reason code.</summary>
    private static TPacket DecodeAck<TPacket>(
        ref MqttSpanReader reader,
        byte flags,
        MqttProtocolVersion version,
        byte requiredFlags = 0)
        where TPacket : MqttAcknowledgementPacket, new()
    {
        if (flags != requiredFlags)
        {
            throw MqttProtocolException.Malformed("Invalid acknowledgement header flags.");
        }

        var packetId = reader.ReadUInt16();
        if (packetId == 0)
        {
            throw MqttProtocolException.Protocol("Packet identifier must not be zero.");
        }

        if (version == MqttProtocolVersion.V311)
        {
            if (!reader.IsEmpty)
            {
                throw MqttProtocolException.Malformed("MQTT 3.1.1 acknowledgement has trailing bytes.");
            }

            return new TPacket { PacketId = packetId };
        }

        byte reasonCode = 0;
        var properties = new MqttProperties();

        if (!reader.IsEmpty)
        {
            reasonCode = reader.ReadByte();
            if (!reader.IsEmpty)
            {
                properties = MqttProperties.Read(ref reader);
            }
        }

        if (!reader.IsEmpty)
        {
            throw MqttProtocolException.Malformed("Acknowledgement contains trailing bytes.");
        }

        MqttPropertyValidation.Validate(typeof(TPacket) == typeof(PubRelPacket) ? MqttPacketType.PubRel : MqttPacketType.PubAck, properties, version);

        return new TPacket
        {
            PacketId = packetId,
            ReasonCode = reasonCode,
            Properties = properties,
        };
    }

    /// <summary>Reads SUBSCRIBE and the subscription options of every filter.</summary>
    private static SubscribePacket DecodeSubscribe(ref MqttSpanReader reader, byte flags, MqttProtocolVersion version)
    {
        if (flags != 0x02)
        {
            throw MqttProtocolException.Malformed("SUBSCRIBE header flags must be 0x02.");
        }

        var packetId = reader.ReadUInt16();
        if (packetId == 0)
        {
            throw MqttProtocolException.Protocol("SUBSCRIBE packet identifier must not be zero.");
        }

        var properties = version == MqttProtocolVersion.V500 ? MqttProperties.Read(ref reader) : new MqttProperties();

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.Subscribe, properties, version);
        }

        var subscriptions = new List<MqttSubscriptionRequest>();

        while (!reader.IsEmpty)
        {
            var filter = reader.ReadString();
            var options = MqttSubscriptionOptions.Decode(reader.ReadByte());
            subscriptions.Add(new MqttSubscriptionRequest(filter, options));
        }

        if (subscriptions.Count == 0)
        {
            throw MqttProtocolException.Protocol("SUBSCRIBE must contain at least one subscription.");
        }

        return new SubscribePacket
        {
            PacketId = packetId,
            Properties = properties,
            Subscriptions = subscriptions,
        };
    }

    /// <summary>Reads one reason code per requested filter.</summary>
    private static SubAckPacket DecodeSubAck(ref MqttSpanReader reader, byte flags, MqttProtocolVersion version)
    {
        if (flags != 0)
        {
            throw MqttProtocolException.Malformed("SUBACK reserved header flags must be zero.");
        }

        var packetId = reader.ReadUInt16();
        if (packetId == 0)
        {
            throw MqttProtocolException.Protocol("SUBACK packet identifier must not be zero.");
        }

        var properties = version == MqttProtocolVersion.V500 ? MqttProperties.Read(ref reader) : new MqttProperties();

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.SubAck, properties, version);
        }

        var codes = new List<byte>();
        while (!reader.IsEmpty)
        {
            codes.Add(reader.ReadByte());
        }

        if (codes.Count == 0)
        {
            throw MqttProtocolException.Protocol("SUBACK must contain at least one reason code.");
        }

        return new SubAckPacket
        {
            PacketId = packetId,
            Properties = properties,
            ReasonCodes = codes,
        };
    }

    /// <summary>Reads UNSUBSCRIBE filters until the body is exhausted.</summary>
    private static UnsubscribePacket DecodeUnsubscribe(ref MqttSpanReader reader, byte flags, MqttProtocolVersion version)
    {
        if (flags != 0x02)
        {
            throw MqttProtocolException.Malformed("UNSUBSCRIBE header flags must be 0x02.");
        }

        var packetId = reader.ReadUInt16();
        if (packetId == 0)
        {
            throw MqttProtocolException.Protocol("UNSUBSCRIBE packet identifier must not be zero.");
        }

        var properties = version == MqttProtocolVersion.V500 ? MqttProperties.Read(ref reader) : new MqttProperties();

        if (version == MqttProtocolVersion.V500)
        {
            MqttPropertyValidation.Validate(MqttPacketType.Unsubscribe, properties, version);
        }

        var filters = new List<string>();
        while (!reader.IsEmpty)
        {
            filters.Add(reader.ReadString());
        }

        if (filters.Count == 0)
        {
            throw MqttProtocolException.Protocol("UNSUBSCRIBE must contain at least one filter.");
        }

        return new UnsubscribePacket
        {
            PacketId = packetId,
            Properties = properties,
            Filters = filters,
        };
    }

    /// <summary>Reads UNSUBACK; 3.1.1 carries only the packet identifier.</summary>
    private static UnsubAckPacket DecodeUnsubAck(ref MqttSpanReader reader, byte flags, MqttProtocolVersion version)
    {
        if (flags != 0)
        {
            throw MqttProtocolException.Malformed("UNSUBACK reserved header flags must be zero.");
        }

        var packetId = reader.ReadUInt16();
        if (packetId == 0)
        {
            throw MqttProtocolException.Protocol("UNSUBACK packet identifier must not be zero.");
        }

        if (version == MqttProtocolVersion.V311)
        {
            if (!reader.IsEmpty)
            {
                throw MqttProtocolException.Malformed("MQTT 3.1.1 UNSUBACK has no payload.");
            }

            return new UnsubAckPacket { PacketId = packetId };
        }

        var properties = MqttProperties.Read(ref reader);
        MqttPropertyValidation.Validate(MqttPacketType.UnsubAck, properties, version);

        var codes = new List<byte>();
        while (!reader.IsEmpty)
        {
            codes.Add(reader.ReadByte());
        }

        if (codes.Count == 0)
        {
            throw MqttProtocolException.Protocol("UNSUBACK must contain at least one reason code.");
        }

        return new UnsubAckPacket
        {
            PacketId = packetId,
            Properties = properties,
            ReasonCodes = codes,
        };
    }

    /// <summary>Handles packets whose body is just a reason code and optional properties.</summary>
    private static TPacket DecodeEmpty<TPacket>(ref MqttSpanReader reader, byte flags)
        where TPacket : MqttPacket, new()
    {
        if (flags != 0)
        {
            throw MqttProtocolException.Malformed("Reserved header flags must be zero.");
        }

        if (!reader.IsEmpty)
        {
            throw MqttProtocolException.Malformed("Packet must not contain a body.");
        }

        return new TPacket();
    }

    /// <summary>Reads DISCONNECT, including the 5.0 reason code and properties.</summary>
    private static DisconnectPacket DecodeDisconnect(ref MqttSpanReader reader, byte flags, MqttProtocolVersion version)
    {
        if (flags != 0)
        {
            throw MqttProtocolException.Malformed("DISCONNECT reserved header flags must be zero.");
        }

        if (version == MqttProtocolVersion.V311)
        {
            if (!reader.IsEmpty)
            {
                throw MqttProtocolException.Malformed("MQTT 3.1.1 DISCONNECT has no payload.");
            }

            return new DisconnectPacket();
        }

        if (reader.IsEmpty)
        {
            return new DisconnectPacket();
        }

        var reasonCode = reader.ReadByte();
        var properties = reader.IsEmpty ? new MqttProperties() : MqttProperties.Read(ref reader);

        if (!reader.IsEmpty)
        {
            throw MqttProtocolException.Malformed("DISCONNECT contains trailing bytes.");
        }

        MqttPropertyValidation.Validate(MqttPacketType.Disconnect, properties, version);

        return new DisconnectPacket { ReasonCode = reasonCode, Properties = properties };
    }

    /// <summary>Reads AUTH, which only exists in MQTT 5.</summary>
    private static AuthPacket DecodeAuth(ref MqttSpanReader reader, byte flags, MqttProtocolVersion version)
    {
        if (version != MqttProtocolVersion.V500)
        {
            throw MqttProtocolException.Protocol("AUTH is only defined in MQTT 5.");
        }

        if (flags != 0)
        {
            throw MqttProtocolException.Malformed("AUTH reserved header flags must be zero.");
        }

        if (reader.IsEmpty)
        {
            return new AuthPacket();
        }

        var reasonCode = reader.ReadByte();
        var properties = reader.IsEmpty ? new MqttProperties() : MqttProperties.Read(ref reader);

        if (!reader.IsEmpty)
        {
            throw MqttProtocolException.Malformed("AUTH contains trailing bytes.");
        }

        MqttPropertyValidation.Validate(MqttPacketType.Auth, properties, version);

        return new AuthPacket { ReasonCode = reasonCode, Properties = properties };
    }
}
