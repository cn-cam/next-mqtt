using System.Collections.Concurrent;
using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker;
using NextMqtt.Broker.Clustering;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Broker.Sessions;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

internal sealed class FakeConnection : IClientConnection
{
    public ConcurrentQueue<MqttPacket> Sent { get; } = new();

    public string RemoteAddress => "test-remote";

    public MqttProtocolVersion ProtocolVersion { get; set; } = MqttProtocolVersion.V500;

    public bool Disconnected { get; private set; }

    public byte DisconnectReason { get; private set; }

    public bool TrySend(MqttPacket packet)
    {
        Sent.Enqueue(packet);
        return true;
    }

    public Task DisconnectAsync(byte reasonCode, string reason, CancellationToken cancellationToken = default)
    {
        Disconnected = true;
        DisconnectReason = reasonCode;
        return Task.CompletedTask;
    }

    public List<PublishPacket> Publishes()
        => Sent.OfType<PublishPacket>().ToList();

    public T? Last<T>()
        where T : MqttPacket
        => Sent.OfType<T>().LastOrDefault();
}

internal sealed class AllowAllAuthenticator : IClientAuthenticator
{
    public ValueTask<AuthenticationResult> AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(AuthenticationResult.Success(
            ClientPrincipal.Create(context.Username ?? context.ClientId, context.ClientId, isSuperuser: true)));
}

internal sealed class AllowAllAuthorizer : ITopicAuthorizer
{
    public bool CanPublish(ClientPrincipal principal, string topic) => true;

    public bool CanSubscribe(ClientPrincipal principal, string filter) => true;
}

internal static class TestBroker
{
    public static MqttBroker Create(
        BrokerOptions? options = null,
        IClientAuthenticator? authenticator = null,
        ITopicAuthorizer? authorizer = null,
        NextMqtt.Broker.Clustering.IClusterRouter? cluster = null,
        NextMqtt.Broker.Storage.ISessionSnapshotStore? sessionStore = null,
        NextMqtt.Broker.Integrations.RuleEngine? rules = null,
        NextMqtt.Broker.Integrations.WebhookDispatcher? webhooks = null,
        NextMqtt.Broker.Storage.ISessionReplicaStore? replicaStore = null)
    {
        options ??= new BrokerOptions
        {
            SweepInterval = TimeSpan.FromSeconds(1),
            RetransmitInterval = TimeSpan.FromSeconds(1),
        };

        return new MqttBroker(
            options,
            authenticator ?? new AllowAllAuthenticator(),
            authorizer ?? new AllowAllAuthorizer(),
            cluster ?? NullClusterRouter.Instance,
            NullLogger<MqttBroker>.Instance,
            retainedJournal: null,
            sessionStore: sessionStore,
            rules: rules,
            webhooks: webhooks,
            replicaStore: replicaStore);
    }

    public static ConnectPacket Connect(
        string clientId,
        MqttProtocolVersion version = MqttProtocolVersion.V500,
        bool cleanStart = true,
        uint sessionExpiry = 0,
        MqttWill? will = null)
    {
        var properties = new MqttProperties();

        if (version == MqttProtocolVersion.V500 && sessionExpiry > 0)
        {
            properties.AddUInt32(MqttPropertyId.SessionExpiryInterval, sessionExpiry);
        }

        return new ConnectPacket
        {
            ClientId = clientId,
            ProtocolVersion = version,
            CleanStart = cleanStart,
            KeepAliveSeconds = 60,
            Properties = properties,
            Will = will,
        };
    }

    public static async Task<(MqttBroker Broker, BrokerSession Session, FakeConnection Connection)> ConnectAsync(
        string clientId,
        MqttProtocolVersion version = MqttProtocolVersion.V500,
        bool cleanStart = true,
        uint sessionExpiry = 0,
        MqttWill? will = null,
        MqttBroker? broker = null)
    {
        broker ??= Create();
        var connection = new FakeConnection { ProtocolVersion = version };
        var connect = Connect(clientId, version, cleanStart, sessionExpiry, will);
        var result = await broker.AttachSessionAsync(connect, ClientPrincipal.Anonymous(clientId), connection);
        return (broker, result.Session, connection);
    }
}
