using NextMqtt.Broker;

namespace NextMqtt.Server.Hosting;

/// <summary>Starts and stops broker background services with the host.</summary>
public sealed class BrokerLifecycleService : IHostedService
{
    private readonly MqttBroker _broker;

    public BrokerLifecycleService(MqttBroker broker)
    {
        _broker = broker;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _broker.StartAsync(cancellationToken);

    // The TCP listener drains inflight traffic while it stops; the broker only
    // has to halt its background loops here.
    public Task StopAsync(CancellationToken cancellationToken) => _broker.StopAsync();
}
