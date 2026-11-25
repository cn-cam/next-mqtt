using NextMqtt.Broker;

namespace NextMqtt.Server.Hosting;

/// <summary>Starts and stops broker background services with the host.</summary>
/// <remarks>只把宿主生命周期转给 Broker；连接排空由 TCP 监听器在停止阶段完成。</remarks>
public sealed class BrokerLifecycleService : IHostedService
{
    /// <summary>Broker whose background loops follow the host lifetime.</summary>
    private readonly MqttBroker _broker;

    /// <summary>Creates the lifecycle bridge.</summary>
    /// <remarks>Broker 是单例，这里只保存引用，不在构造时启动任何循环。</remarks>
    /// <param name="broker">Broker whose loops follow the host lifetime.</param>
    public BrokerLifecycleService(MqttBroker broker)
    {
        _broker = broker;
    }

    /// <summary>Starts broker maintenance and the cluster router.</summary>
    /// <remarks>不阻塞宿主启动：清扫与心跳都在后台任务里跑。</remarks>
    /// <param name="cancellationToken">Cancels startup.</param>
    public Task StartAsync(CancellationToken cancellationToken) => _broker.StartAsync(cancellationToken);

    // The TCP listener drains inflight traffic while it stops; the broker only
    // has to halt its background loops here.
    /// <summary>Stops broker loops; the TCP listener drains connections first.</summary>
    /// <remarks>宿主按注册顺序反向停止，所以监听器会先把连接关掉，这里只停后台循环。</remarks>
    /// <param name="cancellationToken">Ignored.</param>
    public Task StopAsync(CancellationToken cancellationToken) => _broker.StopAsync();
}
