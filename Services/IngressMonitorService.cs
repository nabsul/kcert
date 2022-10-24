using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class IngressMonitorService : IHostedService
{
    private readonly ILogger<IngressMonitorService> _log;
    private readonly K8sClient _k8s;
    private readonly K8sWatchClient _watch;
    private readonly CertChangeService _certChange;
    private readonly KCertConfig _cfg;
    private readonly ExponentialBackoff _exp;

    public IngressMonitorService(ILogger<IngressMonitorService> log, K8sClient k8s, KCertConfig cfg, ExponentialBackoff exp, K8sWatchClient watch, CertChangeService certChange)
    {
        _log = log;
        _k8s = k8s;
        _cfg = cfg;
        _exp = exp;
        _watch = watch;
        _certChange = certChange;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cfg.WatchIngresses)
        {
            _log.LogInformation("Watching for ingress is enabled");
            var action = () => WatchIngressesAsync(cancellationToken);
            _ = _exp.DoWithExponentialBackoffAsync("Watch ingresses", action, cancellationToken);
        }
        else
        {
            _log.LogInformation("Watching for ingress is disabled");
        }

        return Task.CompletedTask;
    }

    private async Task WatchIngressesAsync(CancellationToken tok)
    {
        _log.LogInformation("Watching for ingress changes");
        await _watch.WatchIngressesAsync((t, i) => HandleIngressEventAsync(t, i, tok), tok);
    }

    private Task HandleIngressEventAsync(WatchEventType type, V1Ingress ingress, CancellationToken tok)
    {
        try
        {
            _log.LogInformation("Ingress change event [{type}] for {ns}-{name}", type, ingress.Namespace(), ingress.Name());
            _certChange.RunCheck();
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "Ingress watch event handler cancelled.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ingress watch event handler failed unexpectedly");
        }

        return Task.CompletedTask;
    }
}
