using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Security;

namespace NextMqtt.Broker.Integrations;

/// <summary>External plugin loading settings.</summary>
/// <remarks>插件与 Broker 同进程、同权限，这里不做沙箱。</remarks>
public sealed class PluginOptions
{
    /// <summary>Configuration section name (<c>Plugins</c>).</summary>
    /// <remarks>环境变量前缀 NEXTMQTT_PLUGINS__。</remarks>
    public const string SectionName = "Plugins";

    /// <summary>Load plugins at startup.</summary>
    /// <remarks>关闭时完全不扫描目录，配好的类型名也不会生效。</remarks>
    public bool Enabled { get; set; }

    /// <summary>Directory scanned for plugin assemblies and their manifests.</summary>
    /// <remarks>递归查找 plugin.json；相对路径按进程工作目录解析。</remarks>
    public string Directory { get; set; } = "plugins";

    /// <summary>Full type name of the authenticator plugin to use, when any.</summary>
    /// <remarks>留空则取第一个实现认证契约的类型。</remarks>
    public string? Authenticator { get; set; }

    /// <summary>Full type name of the authorizer plugin to use, when any.</summary>
    /// <remarks>同上；类型名区分大小写。</remarks>
    public string? Authorizer { get; set; }
}

/// <summary>A plugin assembly that passed manifest validation.</summary>
/// <remarks>只有清单完整且契约版本匹配的才会出现在这里。</remarks>
/// <param name="Name">Manifest name.</param>
/// <param name="Version">Manifest version.</param>
/// <param name="Directory">Folder the assembly was loaded from.</param>
/// <param name="Assembly">Loaded assembly.</param>
public sealed record LoadedPlugin(string Name, string Version, string Directory, Assembly Assembly);

/// <summary>
/// Loads first-party plugins from a directory into a collectible load context.
/// </summary>
/// <remarks>
/// 插件运行在 Broker 进程内，拥有与 Broker 相同的权限；这里只做加载与隔离，
/// 不声称沙箱。契约程序集与宿主共享，避免同一接口出现两份类型身份。
/// </remarks>
public sealed class PluginHost : IDisposable
{
    /// <summary>Contract version this host accepts.</summary>
    /// <remarks>清单里的版本必须完全相等，不做兼容范围。</remarks>
    public const int ContractVersion = 1;

    /// <summary>Load contexts, kept so they can be unloaded later.</summary>
    private readonly List<PluginLoadContext> _contexts = [];
    /// <summary>Plugins that loaded successfully.</summary>
    private readonly List<LoadedPlugin> _plugins = [];
    /// <summary>Objects created from plugin assemblies, searched by contract type.</summary>
    private readonly List<object> _instances = [];

    /// <summary>Assemblies whose types must stay identical to the host's.</summary>
    /// <remarks>契约程序集必须共享，否则插件实现的接口在宿主看来是另一个类型。</remarks>
    private static readonly Assembly[] SharedAssemblies =
    [
        typeof(PluginHost).Assembly,
        typeof(Core.Protocol.MqttProtocolVersion).Assembly,
    ];

    /// <summary>Creates an empty host, used when loading is disabled.</summary>
    private PluginHost()
    {
    }

    /// <summary>Plugins that were loaded successfully.</summary>
    /// <remarks>加载失败的不会出现在列表里，只留日志。</remarks>
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>Instances created from plugin assemblies.</summary>
    /// <remarks>每个公开无参类型一个实例，按程序集顺序排列。</remarks>
    public IReadOnlyList<object> Instances => _instances;

    /// <summary>Scans a directory and loads every valid plugin.</summary>
    /// <remarks>单个插件失败只跳过它，其余继续加载。</remarks>
    /// <param name="options">Directory and enable flag.</param>
    /// <param name="logger">Logger for rejected plugins.</param>
    /// <returns>Host with whatever loaded successfully.</returns>
    public static PluginHost Load(PluginOptions options, ILogger logger)
    {
        var host = new PluginHost();

        if (!options.Enabled)
        {
            return host;
        }

        var directory = Path.GetFullPath(options.Directory);

        if (!System.IO.Directory.Exists(directory))
        {
            logger.LogWarning("Plugin directory {Directory} does not exist", directory);
            return host;
        }

        foreach (var manifestPath in System.IO.Directory.EnumerateFiles(directory, "plugin.json", SearchOption.AllDirectories))
        {
            var folder = Path.GetDirectoryName(manifestPath)!;

            try
            {
                var manifest = JsonSerializer.Deserialize<Manifest>(
                    File.ReadAllText(manifestPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (manifest is null || string.IsNullOrWhiteSpace(manifest.EntryAssembly))
                {
                    logger.LogWarning("Ignoring {Manifest}: no entryAssembly", manifestPath);
                    continue;
                }

                if (manifest.ContractVersion != ContractVersion)
                {
                    logger.LogWarning(
                        "Ignoring plugin {Name}: contract version {Version} is not supported",
                        manifest.Name,
                        manifest.ContractVersion);
                    continue;
                }

                var assemblyPath = Path.Combine(folder, manifest.EntryAssembly);

                if (!File.Exists(assemblyPath))
                {
                    logger.LogWarning("Ignoring plugin {Name}: {Assembly} not found", manifest.Name, manifest.EntryAssembly);
                    continue;
                }

                var context = new PluginLoadContext(assemblyPath, SharedAssemblies);
                var assembly = context.LoadFromAssemblyPath(assemblyPath);
                host._contexts.Add(context);
                host._plugins.Add(new LoadedPlugin(
                    manifest.Name ?? Path.GetFileNameWithoutExtension(assemblyPath),
                    manifest.Version ?? "0.0.0",
                    folder,
                    assembly));

                foreach (var instance in CreateInstances(assembly, logger))
                {
                    host._instances.Add(instance);
                }
            }
            catch (Exception ex) when (ex is IOException or JsonException or BadImageFormatException)
            {
                logger.LogWarning(ex, "Failed to load plugin from {Manifest}", manifestPath);
            }
        }

        return host;
    }

    /// <summary>Finds a loaded authenticator by full type name.</summary>
    /// <remarks>找不到返回 null，调用方回退到内置认证。</remarks>
    /// <param name="typeName">Configured type name, or null.</param>
    public IClientAuthenticator? FindAuthenticator(string? typeName)
        => Find<IClientAuthenticator>(typeName);

    /// <summary>Finds a loaded authorizer by full type name.</summary>
    /// <remarks>同上，回退到配置里的 ACL。</remarks>
    /// <param name="typeName">Configured type name, or null.</param>
    public ITopicAuthorizer? FindAuthorizer(string? typeName)
        => Find<ITopicAuthorizer>(typeName);

    /// <summary>Unloads every plugin context; hosted instances are not disposed.</summary>
    /// <remarks>不调用插件的 Dispose：能卸载不代表能安全清理，由调用方决定。</remarks>
    public void Dispose()
    {
        foreach (var context in _contexts)
        {
            context.Unload();
        }

        _contexts.Clear();
        _instances.Clear();
        _plugins.Clear();
    }

    /// <summary>Searches instances of a contract type by full name.</summary>
    /// <remarks>类型名为空时取第一个匹配契约的实例。</remarks>
    /// <typeparam name="T">Contract type.</typeparam>
    /// <param name="typeName">Full type name, or null for "no preference".</param>
    private T? Find<T>(string? typeName) where T : class
        => _instances
            .OfType<T>()
            .FirstOrDefault(instance => typeName is null
                || string.Equals(instance.GetType().FullName, typeName, StringComparison.Ordinal));

    /// <summary>Instantiates every public parameterless type in the assembly.</summary>
    /// <remarks>只支持无参构造；有依赖的类型由插件自己在内部组装。</remarks>
    /// <param name="assembly">Plugin assembly.</param>
    /// <param name="logger">Logger for construction failures.</param>
    private static IEnumerable<object> CreateInstances(Assembly assembly, ILogger logger)
    {
        Type[] types;

        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            logger.LogWarning(ex, "Plugin {Assembly} exposes types that cannot be resolved", assembly.GetName().Name);
            types = ex.Types.Where(type => type is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (!type.IsClass || type.IsAbstract || type.GetConstructor(Type.EmptyTypes) is null)
            {
                continue;
            }

            object? instance = null;

            try
            {
                instance = Activator.CreateInstance(type);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to construct plugin type {Type}", type.FullName);
            }

            if (instance is not null)
            {
                yield return instance;
            }
        }
    }

    /// <summary>Manifest shape stored next to the plugin assembly.</summary>
    /// <remarks>字段名不区分大小写，未知字段被忽略。</remarks>
    /// <param name="Name">Human-readable plugin name.</param>
    /// <param name="Version">Plugin version.</param>
    /// <param name="ContractVersion">Contract version the plugin was built against.</param>
    /// <param name="EntryAssembly">Assembly file name to load.</param>
    private sealed record Manifest(string? Name, string? Version, int ContractVersion, string? EntryAssembly);

    /// <summary>
    /// Load context that shares contract assemblies with the host and isolates
    /// everything else so the plugin can be unloaded.
    /// </summary>
    /// <remarks>只共享契约与核心协议两个程序集；其余依赖从插件目录解析，各插件之间互不干扰。</remarks>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver? _resolver;
        private readonly Assembly[] _shared;

        /// <summary>Creates a collectible context for one plugin assembly.</summary>
        /// <remarks>可卸载上下文在卸载后释放程序集，但残留引用会让它退化成普通上下文。</remarks>
        /// <param name="assemblyPath">Plugin assembly path.</param>
        /// <param name="shared">Assemblies that must resolve to the host copies.</param>
        public PluginLoadContext(string assemblyPath, Assembly[] shared)
            : base(isCollectible: true)
        {
            _shared = shared;
            _resolver = File.Exists(Path.ChangeExtension(assemblyPath, ".deps.json"))
                ? new AssemblyDependencyResolver(assemblyPath)
                : null;
        }

        /// <summary>Resolves contracts from the host and the rest from the plugin folder.</summary>
        /// <remarks>契约走宿主、其余走插件目录，避免版本漂移。</remarks>
        /// <param name="assemblyName">Requested assembly.</param>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            foreach (var assembly in _shared)
            {
                if (string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.Ordinal))
                {
                    return assembly;
                }
            }

            var path = _resolver?.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
