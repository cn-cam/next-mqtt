using Microsoft.Extensions.Logging;
using NextMqtt.Server.Options;
using NextMqtt.Broker;

namespace NextMqtt.Server.Hosting;

/// <summary>Starts and stops broker background services with the host.</summary>
public sealed class BrokerLifecycleService : IHostedService
{
    private readonly MqttBroker _broker;
    private readonly ServerOptions _options;
    private readonly ILogger<BrokerLifecycleService> _logger;

    public BrokerLifecycleService(
        MqttBroker broker,
        ServerOptions options,
        ILogger<BrokerLifecycleService> logger)
    {
        _broker = broker;
        _options = options;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken) => _broker.StartAsync(cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(0, _options.DrainTimeoutSeconds));

        if (timeout > TimeSpan.Zero)
        {
            var pending = await _broker.DrainAsync(timeout, cancellationToken).ConfigureAwait(false);

            if (pending > 0)
            {
                _logger.LogWarning(
                    "Drain timeout reached with {Pending} session(s) still holding inflight messages",
                    pending);
            }
        }

        await _broker.StopAsync().ConfigureAwait(false);
    }
}
