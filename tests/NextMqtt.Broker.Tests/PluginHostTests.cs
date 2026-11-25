using Microsoft.Extensions.Logging.Abstractions;
using NextMqtt.Broker.Integrations;
using NextMqtt.Broker.Security;
using NextMqtt.Core.Protocol;

namespace NextMqtt.Broker.Tests;

/// <summary>Plugin surface used by <see cref="PluginHostTests"/>.</summary>
public sealed class TestPluginAuthenticator : IClientAuthenticator
{
    /// <summary>Rejects every connection; the host only needs an instance.</summary>
    /// <param name="context">Credentials presented by the client.</param>
    /// <param name="cancellationToken">Ignored.</param>
    public ValueTask<AuthenticationResult> AuthenticateAsync(
        ClientAuthenticationContext context,
        CancellationToken cancellationToken = default)
        => ValueTask.FromResult(AuthenticationResult.Failure(MqttReasonCodes.NotAuthorized));
}

public sealed class PluginHostTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "nextmqtt-plugin-tests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        // The collectible context releases the file only after a GC cycle.
        GC.Collect();
        GC.WaitForPendingFinalizers();

        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Windows may keep the plugin assembly mapped until the process ends.
        }
    }

    private void WriteManifest(int contractVersion, string entryAssembly)
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(
            Path.Combine(_directory, "plugin.json"),
            $$"""
            {
              "name": "tests",
              "version": "1.2.3",
              "contractVersion": {{contractVersion}},
              "entryAssembly": "{{entryAssembly}}"
            }
            """);
    }

    [Fact]
    public void Missing_directory_yields_an_empty_host()
    {
        using var host = PluginHost.Load(
            new PluginOptions { Enabled = true, Directory = Path.Combine(_directory, "nope") },
            NullLogger.Instance);

        Assert.Empty(host.Plugins);
    }

    [Fact]
    public void Unsupported_contract_version_is_rejected()
    {
        WriteManifest(PluginHost.ContractVersion + 1, "NextMqtt.Broker.Tests.dll");

        using var host = PluginHost.Load(
            new PluginOptions { Enabled = true, Directory = _directory },
            NullLogger.Instance);

        Assert.Empty(host.Plugins);
    }

    [Fact]
    public void Valid_manifest_loads_the_assembly_and_discovers_plugins()
    {
        WriteManifest(PluginHost.ContractVersion, "NextMqtt.Broker.Tests.dll");
        File.Copy(
            typeof(TestPluginAuthenticator).Assembly.Location,
            Path.Combine(_directory, "NextMqtt.Broker.Tests.dll"),
            overwrite: true);

        using var host = PluginHost.Load(
            new PluginOptions { Enabled = true, Directory = _directory },
            NullLogger.Instance);

        var plugin = Assert.Single(host.Plugins);
        Assert.Equal("tests", plugin.Name);
        Assert.Equal("1.2.3", plugin.Version);

        var authenticator = host.FindAuthenticator(typeof(TestPluginAuthenticator).FullName);
        Assert.NotNull(authenticator);
        Assert.Equal(typeof(TestPluginAuthenticator).FullName, authenticator.GetType().FullName);
    }
}
