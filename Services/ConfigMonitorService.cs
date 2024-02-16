using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class ConfigMonitorService : IHostedService
{
    private readonly ILogger<ConfigMonitorService> _log;
    private readonly K8sWatchClient _watch;
    private readonly CertChangeService _certChange;
    private readonly KCertConfig _cfg;
    private readonly ExponentialBackoff _exp;

    public ConfigMonitorService(ILogger<ConfigMonitorService> log, KCertConfig cfg, ExponentialBackoff exp, K8sWatchClient k8sWatch, CertChangeService certChange)
    {
        _log = log;
        _cfg = cfg;
        _exp = exp;
        _watch = k8sWatch;
        _certChange = certChange;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cfg.WatchConfigMaps)
        {
            _log.LogInformation("Watching for configmaps is enabled");
            var action = () => WatchConfigMapsAsync(cancellationToken);
            _ = _exp.DoWithExponentialBackoffAsync("Watch configmaps", action, cancellationToken);
        }
        else
        {
            _log.LogInformation("Watching for configmaps is disabled");
        }

        return Task.CompletedTask;
    }

    private async Task WatchConfigMapsAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("Watching for configmaps changes");
        await _watch.WatchConfigMapsAsync((_, _) => HandleConfigMapEventAsync(), cancellationToken);
    }

    private Task HandleConfigMapEventAsync()
    {
        try
        {
            _certChange.RunCheck();
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "ConfigMap event handler cancelled.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ConfigMap event handler failed unexpectedly");
        }

        return Task.CompletedTask;
    }
}
