namespace KCert.Services;

[Service]
public class ConfigMonitorService(ILogger<ConfigMonitorService> log, KCertConfig cfg, ExponentialBackoff exp, K8sWatchClient watch, CertChangeService certChange) : IHostedService
{
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cfg.WatchConfigMaps)
        {
            log.LogInformation("Watching for configmaps is enabled");
            var action = () => WatchConfigMapsAsync(cancellationToken);
            _ = exp.DoWithExponentialBackoffAsync("Watch configmaps", action, cancellationToken);
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
        await watch.WatchConfigMapsAsync((_, _) => HandleConfigMapEventAsync(), cancellationToken);
    }

    private Task HandleConfigMapEventAsync()
    {
        try
        {
            certChange.RunCheck();
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
