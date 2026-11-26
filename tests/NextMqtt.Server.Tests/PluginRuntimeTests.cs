using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker.Integrations;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Core.Protocol;
using NextMqtt.Server.Hosting;

namespace NextMqtt.Server.Tests;

public sealed class PluginRuntimeTests
{
    private sealed class FixedAuthenticator : IClientAuthenticator
    {
        private readonly bool _allow;

        public FixedAuthenticator(bool allow) => _allow = allow;

        public ValueTask<AuthenticationResult> AuthenticateAsync(
            ClientAuthenticationContext context,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(_allow
                ? AuthenticationResult.Success(ClientPrincipal.Anonymous(context.ClientId))
                : AuthenticationResult.Failure(MqttReasonCodes.NotAuthorized));
    }

    private static ClientAuthenticationContext Context()
        => new("client-1", null, ReadOnlyMemory<byte>.Empty, "127.0.0.1", MqttProtocolVersion.V500);

    private static (PluginRuntime Runtime, SwitchableAuthenticator Authenticator, SwitchableAuthorizer Authorizer) Create()
    {
        var authenticator = new SwitchableAuthenticator(
            new ClientAuthenticator(new AuthenticationOptions { AllowAnonymous = true }));
        var authorizer = new SwitchableAuthorizer(
            new TopicAuthorizer(new AclOptions { DefaultPolicy = AclDefaultPolicy.Allow }));

        var runtime = new PluginRuntime(
            new PluginOptions { Enabled = false },
            NullLoggerFactory.Instance,
            authenticator,
            authorizer,
            new ClientAuthenticator(new AuthenticationOptions { AllowAnonymous = true }),
            new TopicAuthorizer(new AclOptions { DefaultPolicy = AclDefaultPolicy.Allow }));

        return (runtime, authenticator, authorizer);
    }

    [Fact]
    public async Task Swap_changes_which_authenticator_is_used()
    {
        var switchable = new SwitchableAuthenticator(new FixedAuthenticator(allow: true));

        Assert.True((await switchable.AuthenticateAsync(Context())).Succeeded);

        switchable.Swap(new FixedAuthenticator(allow: false));

        Assert.False((await switchable.AuthenticateAsync(Context())).Succeeded);
        Assert.Equal(1, switchable.Generation);
    }

    [Fact]
    public void Swap_changes_which_authorizer_is_used()
    {
        var deny = new TopicAuthorizer(new AclOptions { DefaultPolicy = AclDefaultPolicy.Deny });
        var allow = new TopicAuthorizer(new AclOptions { DefaultPolicy = AclDefaultPolicy.Allow });
        var principal = ClientPrincipal.Anonymous("client-1");

        var switchable = new SwitchableAuthorizer(deny);
        Assert.False(switchable.CanSubscribe(principal, "a/#"));

        switchable.Swap(allow);
        Assert.True(switchable.CanSubscribe(principal, "a/#"));
    }

    [Fact]
    public async Task Disabled_plugin_host_keeps_the_builtin_hooks()
    {
        var (runtime, authenticator, _) = Create();

        using (runtime)
        {
            var status = runtime.Status();

            Assert.False(status.Enabled);
            Assert.Empty(status.Plugins);
            Assert.Equal(0, status.Reloads);
            Assert.False(status.AuthenticatorLoaded);
            Assert.False(status.AuthorizerLoaded);

            Assert.True((await authenticator.AuthenticateAsync(Context())).Succeeded);

            var reloaded = runtime.Reload();

            Assert.Equal(1, reloaded.Reloads);
            Assert.True((await authenticator.AuthenticateAsync(Context())).Succeeded);
        }
    }
}
