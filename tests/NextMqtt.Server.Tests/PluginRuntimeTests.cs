using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker.Integrations;
using NextMqtt.Broker.Options;
using NextMqtt.Broker.Security;
using NextMqtt.Core.Protocol;
using NextMqtt.Server.Hosting;

namespace NextMqtt.Server.Tests;

/// <summary>Plugin type used by the reload test; the host instantiates it from a copy of this assembly.</summary>
public sealed class ReloadProbePlugin : NextMqtt.Broker.Integrations.IPluginLifecycle
{
    /// <summary>Nothing to prepare.</summary>
    /// <param name="context">Load information supplied by the host.</param>
    public void Initialize(NextMqtt.Broker.Integrations.PluginContext context)
    {
    }

    /// <summary>Nothing to release.</summary>
    public void Dispose()
    {
    }
}

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
            () => new PluginOptions { Enabled = false },
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

    [Fact]
    public void Reload_reads_the_settings_again()
    {
        var directory = Path.Combine(Path.GetTempPath(), "nextmqtt-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "plugin.json"),
                $$"""
                {
                  "name": "reload",
                  "version": "1.0.0",
                  "contractVersion": {{NextMqtt.Broker.Integrations.PluginHost.ContractVersion}},
                  "entryAssembly": "NextMqtt.Server.Tests.dll"
                }
                """);

            File.Copy(
                typeof(ReloadProbePlugin).Assembly.Location,
                Path.Combine(directory, "NextMqtt.Server.Tests.dll"),
                overwrite: true);

            var options = new PluginOptions { Enabled = false };
            var authenticator = new SwitchableAuthenticator(
                new ClientAuthenticator(new AuthenticationOptions { AllowAnonymous = true }));
            var authorizer = new SwitchableAuthorizer(
                new TopicAuthorizer(new AclOptions { DefaultPolicy = AclDefaultPolicy.Allow }));

            using var runtime = new PluginRuntime(
                () => options,
                NullLoggerFactory.Instance,
                authenticator,
                authorizer,
                new ClientAuthenticator(new AuthenticationOptions { AllowAnonymous = true }),
                new TopicAuthorizer(new AclOptions { DefaultPolicy = AclDefaultPolicy.Allow }));

            Assert.Empty(runtime.Status().Plugins);

            options = new PluginOptions { Enabled = true, Directory = directory };

            var status = runtime.Reload();

            Assert.True(status.Enabled);
            Assert.Single(status.Plugins);
            Assert.Equal("reload", status.Plugins[0].Name);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The load context may keep the assembly mapped on Windows.
            }
        }
    }
}
