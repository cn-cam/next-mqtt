namespace NextMqtt.Broker.Integrations;

/// <summary>Context handed to a plugin when it is initialised.</summary>
/// <remarks>只暴露加载信息，不注入宿主服务：插件应自行管理依赖与生命周期。</remarks>
/// <param name="Directory">Folder the plugin was loaded from.</param>
/// <param name="ContractVersion">Contract version the host accepted.</param>
public sealed record PluginContext(string Directory, int ContractVersion);

/// <summary>Optional lifecycle hook implemented by plugins that hold resources.</summary>
/// <remarks>
/// 实现该接口的实例会在加载后收到 <see cref="Initialize"/>，在重载或关闭时收到
/// <see cref="Dispose"/>，且 Dispose 早于卸载上下文，因此可以安全释放文件与线程。
/// 未实现该接口的插件保持原有行为：只创建实例，不做清理。
/// </remarks>
public interface IPluginLifecycle
{
    /// <summary>Called once after the instance is created.</summary>
    /// <remarks>抛异常视为插件加载失败：该插件会被跳过，其余插件继续加载。</remarks>
    /// <param name="context">Where the plugin was loaded from.</param>
    void Initialize(PluginContext context);

    /// <summary>Called before the plugin's load context is unloaded.</summary>
    /// <remarks>必须幂等；重载失败或宿主关闭时都可能被调用一次。</remarks>
    void Dispose();
}
