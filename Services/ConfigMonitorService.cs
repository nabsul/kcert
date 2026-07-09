using Polly;

namespace KCert.Services;

public class ConfigMonitorService(ILogger<ConfigMonitorService> log, KCertConfig cfg, K8sWatchClient watch, CertChangeService certChange) : IHostedService
{
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cfg.WatchConfigMaps)
        {
            log.LogInformation("Watching for configmaps is enabled");
            Task action() => WatchConfigMapsAsync(cancellationToken);
            _ = Policy.Handle<Exception>()
                .WaitAndRetryAsync(5, i => TimeSpan.FromSeconds(Math.Pow(2, i)))
                .ExecuteAsync(async ct => await action(), cancellationToken);
        }
        else
        {
            log.LogInformation("Watching for configmaps is disabled");
        }

        return Task.CompletedTask;
    }

    private async Task WatchConfigMapsAsync(CancellationToken cancellationToken)
    {
        log.LogInformation("Watching for configmaps changes");
        await watch.WatchConfigMapsAsync((_, _, t) => HandleConfigMapEventAsync(t), cancellationToken);
    }

    private Task HandleConfigMapEventAsync(CancellationToken tok)
    {
        try
        {
            certChange.RunCheck(tok);
        }
        catch (TaskCanceledException ex)
        {
            log.LogError(ex, "ConfigMap event handler cancelled.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "ConfigMap event handler failed unexpectedly");
        }

        return Task.CompletedTask;
    }
}
