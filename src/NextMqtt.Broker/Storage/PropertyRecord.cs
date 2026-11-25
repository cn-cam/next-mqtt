using System.Globalization;
using System.Text.Json;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Storage;

/// <summary>JSON-friendly form of one MQTT 5 property.</summary>
public sealed class PropertyRecord
{
    private static readonly JsonSerializerOptions SerializerOptions = new();

    /// <summary>MQTT 5 property identifier.</summary>
    public int Id { get; set; }

    /// <summary>Wire encoding of the value, from <see cref="MqttPropertyDataType"/>.</summary>
    public int Type { get; set; }

    /// <summary>Value rendered as text; binary values are Base64.</summary>
    public string? Value { get; set; }

    /// <summary>Serializes a property bag, dropping message expiry (stored absolutely).</summary>
    public static List<PropertyRecord>? FromProperties(MqttProperties properties)
    {
        if (properties.IsEmpty)
        {
            return null;
        }

        var records = new List<PropertyRecord>(properties.Count);

        foreach (var property in properties.Items)
        {
            if (property.Id == MqttPropertyId.MessageExpiryInterval)
            {
                continue;
            }

            var type = MqttProperties.GetDataType(property.Id);
            var record = new PropertyRecord { Id = (int)property.Id, Type = (int)type };

            record.Value = type switch
            {
                MqttPropertyDataType.String => property.Value as string,
                MqttPropertyDataType.Binary => Convert.ToBase64String((byte[])property.Value),
                MqttPropertyDataType.StringPair => JsonSerializer.Serialize(
                    (KeyValuePair<string, string>)property.Value,
                    SerializerOptions),
                _ => Convert.ToString(property.Value, CultureInfo.InvariantCulture),
            };

            records.Add(record);
        }

        return records.Count == 0 ? null : records;
    }

    /// <summary>Rebuilds a property bag; unknown or invalid entries are skipped.</summary>
    public static MqttProperties ToProperties(List<PropertyRecord>? records)
    {
        var properties = new MqttProperties();

        if (records is null)
        {
            return properties;
        }

        foreach (var record in records)
        {
            var id = (MqttPropertyId)record.Id;
            var value = record.Value ?? string.Empty;

            try
            {
                switch ((MqttPropertyDataType)record.Type)
                {
                    case MqttPropertyDataType.Byte:
                        properties.AddByte(id, byte.Parse(value, CultureInfo.InvariantCulture));
                        break;
                    case MqttPropertyDataType.UInt16:
                        properties.AddUInt16(id, ushort.Parse(value, CultureInfo.InvariantCulture));
                        break;
                    case MqttPropertyDataType.UInt32:
                        properties.AddUInt32(id, uint.Parse(value, CultureInfo.InvariantCulture));
                        break;
                    case MqttPropertyDataType.VariableByteInteger:
                        properties.AddVariableByteInteger(id, int.Parse(value, CultureInfo.InvariantCulture));
                        break;
                    case MqttPropertyDataType.String:
                        properties.AddString(id, value);
                        break;
                    case MqttPropertyDataType.Binary:
                        properties.AddBinary(id, Convert.FromBase64String(value));
                        break;
                    case MqttPropertyDataType.StringPair:
                        var pair = JsonSerializer.Deserialize<KeyValuePair<string, string>>(value, SerializerOptions);
                        properties.Add(id, pair);
                        break;
                }
            }
            catch (Exception ex) when (ex is FormatException or JsonException or MqttProtocolException)
            {
                // Skip a property that no longer validates instead of dropping the record.
            }
        }

        return properties;
    }
}
