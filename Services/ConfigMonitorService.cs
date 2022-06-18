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
    private readonly KCertClient _kcert;
    private readonly K8sWatchClient _watch;
    private readonly KCertConfig _cfg;
    private readonly ExponentialBackoff _exp;

    public ConfigMonitorService(ILogger<ConfigMonitorService> log, KCertClient kcert, KCertConfig cfg, ExponentialBackoff exp, K8sWatchClient k8sWatch)
    {
        _log = log;
        _kcert = kcert;
        _cfg = cfg;
        _exp = exp;
        _watch = k8sWatch;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cfg.WatchIngresses)
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
        await _watch.WatchConfigMapsAsync(HandleConfigMapEventAsync, cancellationToken);
    }

    private async Task HandleConfigMapEventAsync(WatchEventType type, V1ConfigMap config, CancellationToken tok)
    {
        try
        {
            _log.LogInformation("ConfigMap change event [{type}] for {ns}-{name}", type, config.Namespace(), config.Name());
            if (type != WatchEventType.Added && type != WatchEventType.Modified)
            {
                return;
            }

            await HandleCertificateRequestAsync(config, tok);
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "ConfigMap event handler cancelled.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "ConfigMap event handler failed unexpectedly");
        }
    }

    private async Task HandleCertificateRequestAsync(V1ConfigMap config, CancellationToken tok)
    {
        var ns = config.Namespace();
        var name = config.Name();
        
        if (!config.Data.TryGetValue("hosts", out var hostList))
        {
            _log.LogError("ConfigMap {ns}-{n} does not a hosts entry", ns, name);
            return;
        }

        var hosts = hostList.Split(',');
        if (hosts.Length < 1)
        {
            _log.LogError("ConfigMap {ns}-{n} does not contain a list of hosts", ns, name);
            return;
        }

        await _kcert.StartRenewalProcessAsync(ns, name, hosts, tok);
    }
}
