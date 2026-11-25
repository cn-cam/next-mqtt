using NextMqtt.Broker.Integrations;

namespace NextMqtt.Broker.Tests;

public sealed class KafkaBridgeTests
{
    [Fact]
    public void Topics_map_to_dotted_safe_names()
    {
        Assert.Equal("mqtt.sensors.room_1.temp", KafkaBridge.MapTopic("mqtt.", "sensors/room 1/temp"));
        Assert.Equal("mqtt.a.b", KafkaBridge.MapTopic("mqtt.", "a/b"));
        Assert.Equal("mqtt.x_y", KafkaBridge.MapTopic("mqtt.", "x*y"));
        Assert.Equal("plain", KafkaBridge.MapTopic(string.Empty, "plain"));
    }
}
