using NextMqtt.Broker.Security;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Server.Tests;

public class EnhancedAuthTests
{
    /// <summary>Provider that challenges once and accepts the response.</summary>
    private sealed class ChallengeAuthenticator : IEnhancedAuthenticator
    {
        public string Method => "test-challenge";

        public ValueTask<EnhancedAuthResult> AuthenticateAsync(
            EnhancedAuthContext context,
            CancellationToken cancellationToken = default)
        {
            if (context.Data.Span.SequenceEqual("open"u8))
            {
                return ValueTask.FromResult(EnhancedAuthResult.Continue("answer"u8.ToArray()));
            }

            if (context.Data.Span.SequenceEqual("answer"u8))
            {
                return ValueTask.FromResult(EnhancedAuthResult.Success(
                    ClientPrincipal.Create("challenged", context.ClientId, isSuperuser: false)));
            }

            return ValueTask.FromResult(EnhancedAuthResult.Failure(MqttReasonCodes.NotAuthorized));
        }
    }

    private static ConnectPacket Connect(string clientId, string method, string data)
        => new()
        {
            ClientId = clientId,
            ProtocolVersion = MqttProtocolVersion.V500,
            CleanStart = true,
            KeepAliveSeconds = 30,
            Properties = new MqttProperties()
                .AddString(MqttPropertyId.AuthenticationMethod, method)
                .AddBinary(MqttPropertyId.AuthenticationData, System.Text.Encoding.UTF8.GetBytes(data)),
        };

    [Fact]
    public async Task Challenge_exchange_finishes_with_connack()
    {
        await using var server = MqttTestServer.Start(enhanced: [new ChallengeAuthenticator()]);

        await using var client = await MqttTestClient.OpenAsync(server.Port, MqttProtocolVersion.V500);
        await client.SendAsync(Connect("challenge", "test-challenge", "open"));

        var auth = await client.ReadAsync<AuthPacket>();
        Assert.Equal(MqttReasonCodes.ContinueAuthentication, auth.ReasonCode);
        Assert.Equal("test-challenge", auth.Properties.GetString(MqttPropertyId.AuthenticationMethod));

        await client.SendAsync(new AuthPacket
        {
            ReasonCode = MqttReasonCodes.ContinueAuthentication,
            Properties = new MqttProperties()
                .AddString(MqttPropertyId.AuthenticationMethod, "test-challenge")
                .AddBinary(MqttPropertyId.AuthenticationData, "answer"u8.ToArray()),
        });

        var connAck = await client.ReadAsync<ConnAckPacket>();
        Assert.Equal(MqttReasonCodes.Success, connAck.ReasonCode);
    }

    [Fact]
    public async Task Unknown_method_is_rejected_with_bad_authentication_method()
    {
        await using var server = MqttTestServer.Start();

        await using var client = await MqttTestClient.OpenAsync(server.Port, MqttProtocolVersion.V500);
        await client.SendAsync(Connect("unknown", "not-configured", "x"));

        var connAck = await client.ReadAsync<ConnAckPacket>();
        Assert.Equal(MqttReasonCodes.BadAuthenticationMethod, connAck.ReasonCode);
    }
}
