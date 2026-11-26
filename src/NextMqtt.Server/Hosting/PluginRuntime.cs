using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Integrations;
using NextMqtt.Broker.Security;

namespace NextMqtt.Server.Hosting;

/// <summary>One loaded plugin as reported to the dashboard.</summary>
/// <remarks>字段名就是 <c>GET /api/v1/plugins</c> 的契约。</remarks>
/// <param name="Name">Manifest name.</param>
/// <param name="Version">Manifest version.</param>
/// <param name="Directory">Folder the assembly was loaded from.</param>
/// <param name="Assembly">Assembly file name.</param>
public sealed record PluginInfo(string Name, string Version, string Directory, string? Assembly);

/// <summary>Current plugin state, including which hooks are active.</summary>
/// <remarks>每次请求现算，重载后立即反映新状态。</remarks>
/// <param name="Enabled">Whether plugin loading is configured.</param>
/// <param name="Directory">Directory that is scanned.</param>
/// <param name="ContractVersion">Contract version accepted by this host.</param>
/// <param name="Authenticator">Configured authenticator type name, if any.</param>
/// <param name="Authorizer">Configured authorizer type name, if any.</param>
/// <param name="AuthenticatorLoaded">True when a plugin provides the authenticator.</param>
/// <param name="AuthorizerLoaded">True when a plugin provides the authorizer.</param>
/// <param name="Instances">Objects created from plugin assemblies.</param>
/// <param name="Reloads">How many times the host has been reloaded.</param>
/// <param name="Plugins">Successfully loaded plugins.</param>
public sealed record PluginStatus(
    bool Enabled,
    string Directory,
    int ContractVersion,
    string? Authenticator,
    string? Authorizer,
    bool AuthenticatorLoaded,
    bool AuthorizerLoaded,
    int Instances,
    long Reloads,
    IReadOnlyList<PluginInfo> Plugins);

/// <summary>Owns the plugin host and swaps it on reload.</summary>
/// <remarks>
/// 重载时先加载新宿主，再原子替换认证器与授权器，最后卸载旧宿主。
/// 旧的调用可能仍在运行，因此卸载只是释放可卸载上下文，不会中断在途请求。
/// </remarks>
public sealed class PluginRuntime : IDisposable
{
    /// <summary>Re-reads the plugin section, so a reload picks up edited settings.</summary>
    private readonly Func<PluginOptions> _optionsProvider;

    /// <summary>Settings used for the current host.</summary>
    private PluginOptions _options;

    /// <summary>Factory used to give each host its own logger.</summary>
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Logger for reload results.</summary>
    private readonly ILogger<PluginRuntime> _logger;

    /// <summary>Switch that receives the active authenticator.</summary>
    private readonly SwitchableAuthenticator _authenticator;

    /// <summary>Switch that receives the active authorizer.</summary>
    private readonly SwitchableAuthorizer _authorizer;

    /// <summary>Authenticator used when no plugin provides one.</summary>
    private readonly IClientAuthenticator _builtinAuthenticator;

    /// <summary>Authorizer used when no plugin provides one.</summary>
    private readonly ITopicAuthorizer _builtinAuthorizer;

    /// <summary>Guards the host reference during reload.</summary>
    private readonly object _gate = new();

    /// <summary>Currently loaded plugin host.</summary>
    private PluginHost _host;

    /// <summary>Completed reload count.</summary>
    private long _reloads;

    /// <summary>Loads the initial plugin set.</summary>
    /// <remarks>构造即加载；目录不存在时得到一个空宿主，不影响启动。</remarks>
    /// <param name="optionsProvider">Reads the current plugin settings; called on every reload.</param>
    /// <param name="loggerFactory">Factory used for plugin-host logging.</param>
    /// <param name="authenticator">Switch that receives the plugin or built-in authenticator.</param>
    /// <param name="authorizer">Switch that receives the plugin or built-in authorizer.</param>
    /// <param name="builtinAuthenticator">Authenticator used when no plugin provides one.</param>
    /// <param name="builtinAuthorizer">Authorizer used when no plugin provides one.</param>
    public PluginRuntime(
        Func<PluginOptions> optionsProvider,
        ILoggerFactory loggerFactory,
        SwitchableAuthenticator authenticator,
        SwitchableAuthorizer authorizer,
        IClientAuthenticator builtinAuthenticator,
        ITopicAuthorizer builtinAuthorizer)
    {
        _optionsProvider = optionsProvider;
        _options = optionsProvider();
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<PluginRuntime>();
        _authenticator = authenticator;
        _authorizer = authorizer;
        _builtinAuthenticator = builtinAuthenticator;
        _builtinAuthorizer = builtinAuthorizer;
        _host = PluginHost.Load(_options, loggerFactory.CreateLogger<PluginHost>());

        Apply(_host);
    }

    /// <summary>Rescans the plugin directory and swaps the active hooks.</summary>
    /// <remarks>失败不会回滚：新宿主加载失败时退回内置实现，Broker 继续服务。</remarks>
    /// <returns>The state after reload.</returns>
    public PluginStatus Reload()
    {
        PluginHost? previous;
        PluginOptions options;

        lock (_gate)
        {
            options = _optionsProvider();
            previous = _host;
            _host = PluginHost.Load(options, _loggerFactory.CreateLogger<PluginHost>());
            _options = options;
            Apply(_host);
            Interlocked.Increment(ref _reloads);
        }

        previous.Dispose();

        _logger.LogInformation(
            "Plugin host reloaded: {Count} plugin(s), authenticator={Authenticator}, authorizer={Authorizer}",
            _host.Plugins.Count,
            options.Authenticator ?? "built-in",
            options.Authorizer ?? "built-in");

        return Status();
    }

    /// <summary>Reports the current plugin state.</summary>
    /// <remarks>与重载互斥，读到的宿主与返回的计数属于同一时刻。</remarks>
    public PluginStatus Status()
    {
        PluginHost host;
        PluginOptions options;

        lock (_gate)
        {
            host = _host;
            options = _options;
        }

        return new PluginStatus(
            options.Enabled,
            options.Directory,
            PluginHost.ContractVersion,
            options.Authenticator,
            options.Authorizer,
            host.FindAuthenticator(options.Authenticator) is not null,
            host.FindAuthorizer(options.Authorizer) is not null,
            host.Instances.Count,
            Interlocked.Read(ref _reloads),
            host.Plugins
                .Select(plugin => new PluginInfo(
                    plugin.Name,
                    plugin.Version,
                    plugin.Directory,
                    plugin.Assembly.GetName().Name))
                .ToList());
    }

    /// <summary>Releases the loaded plugin contexts.</summary>
    /// <remarks>由依赖注入在宿主关闭时调用；重复调用安全。</remarks>
    public void Dispose()
    {
        lock (_gate)
        {
            _host.Dispose();
        }
    }

    /// <summary>Points the switches at the plugin hooks, falling back to built-ins.</summary>
    /// <param name="host">Host that was just loaded.</param>
    private void Apply(PluginHost host)
    {
        _authenticator.Swap(host.FindAuthenticator(_options.Authenticator) ?? _builtinAuthenticator);
        _authorizer.Swap(host.FindAuthorizer(_options.Authorizer) ?? _builtinAuthorizer);
    }
}
