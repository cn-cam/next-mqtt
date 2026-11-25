using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NextMqtt.Broker.Security;

namespace NextMqtt.Broker.Integrations;

/// <summary>External plugin loading settings.</summary>
public sealed class PluginOptions
{
    /// <summary>Configuration section name (<c>Plugins</c>).</summary>
    public const string SectionName = "Plugins";

    /// <summary>Load plugins at startup.</summary>
    public bool Enabled { get; set; }

    /// <summary>Directory scanned for plugin assemblies and their manifests.</summary>
    public string Directory { get; set; } = "plugins";

    /// <summary>Full type name of the authenticator plugin to use, when any.</summary>
    public string? Authenticator { get; set; }

    /// <summary>Full type name of the authorizer plugin to use, when any.</summary>
    public string? Authorizer { get; set; }
}

/// <summary>A plugin assembly that passed manifest validation.</summary>
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
    public const int ContractVersion = 1;

    private readonly List<PluginLoadContext> _contexts = [];
    private readonly List<LoadedPlugin> _plugins = [];
    private readonly List<object> _instances = [];

    /// <summary>Assemblies whose types must stay identical to the host's.</summary>
    private static readonly Assembly[] SharedAssemblies =
    [
        typeof(PluginHost).Assembly,
        typeof(Core.Protocol.MqttProtocolVersion).Assembly,
    ];

    private PluginHost()
    {
    }

    /// <summary>Plugins that were loaded successfully.</summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>Instances created from plugin assemblies.</summary>
    public IReadOnlyList<object> Instances => _instances;

    /// <summary>Scans a directory and loads every valid plugin.</summary>
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
    /// <param name="typeName">Configured type name, or null.</param>
    public IClientAuthenticator? FindAuthenticator(string? typeName)
        => Find<IClientAuthenticator>(typeName);

    /// <summary>Finds a loaded authorizer by full type name.</summary>
    /// <param name="typeName">Configured type name, or null.</param>
    public ITopicAuthorizer? FindAuthorizer(string? typeName)
        => Find<ITopicAuthorizer>(typeName);

    /// <summary>Unloads every plugin context; hosted instances are not disposed.</summary>
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
    /// <typeparam name="T">Contract type.</typeparam>
    /// <param name="typeName">Full type name, or null for "no preference".</param>
    private T? Find<T>(string? typeName) where T : class
        => _instances
            .OfType<T>()
            .FirstOrDefault(instance => typeName is null
                || string.Equals(instance.GetType().FullName, typeName, StringComparison.Ordinal));

    /// <summary>Instantiates every public parameterless type in the assembly.</summary>
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
    /// <param name="Name">Human-readable plugin name.</param>
    /// <param name="Version">Plugin version.</param>
    /// <param name="ContractVersion">Contract version the plugin was built against.</param>
    /// <param name="EntryAssembly">Assembly file name to load.</param>
    private sealed record Manifest(string? Name, string? Version, int ContractVersion, string? EntryAssembly);

    /// <summary>
    /// Load context that shares contract assemblies with the host and isolates
    /// everything else so the plugin can be unloaded.
    /// </summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly AssemblyDependencyResolver? _resolver;
        private readonly Assembly[] _shared;

        /// <summary>Creates a collectible context for one plugin assembly.</summary>
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
