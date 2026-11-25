using NextMqtt.Broker;

namespace NextMqtt.Server.Hosting;

/// <summary>Starts and stops broker background services with the host.</summary>
/// <summary>Starts and stops broker background services with the host.</summary>
public sealed class BrokerLifecycleService : IHostedService
{
    private readonly MqttBroker _broker;

    /// <summary>Creates the lifecycle bridge.</summary>
    /// <param name="broker">Broker whose loops follow the host lifetime.</param>
    public BrokerLifecycleService(MqttBroker broker)
    {
        _broker = broker;
    }

    /// <summary>Starts broker maintenance and the cluster router.</summary>
    /// <param name="cancellationToken">Cancels startup.</param>
    public Task StartAsync(CancellationToken cancellationToken) => _broker.StartAsync(cancellationToken);

    // The TCP listener drains inflight traffic while it stops; the broker only
    // has to halt its background loops here.
    /// <summary>Stops broker loops; the TCP listener drains connections first.</summary>
    /// <param name="cancellationToken">Ignored.</param>
    public Task StopAsync(CancellationToken cancellationToken) => _broker.StopAsync();
}
