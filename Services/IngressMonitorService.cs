using k8s;
using k8s.Models;

namespace KCert.Services;

[Service]
public class IngressMonitorService(ILogger<IngressMonitorService> log, KCertConfig cfg, ExponentialBackoff exp, K8sWatchClient watch, CertChangeService certChange) : IHostedService
{
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cfg.WatchIngresses)
        {
            log.LogInformation("Watching for ingress is enabled");
            var action = () => WatchIngressesAsync(cancellationToken);
            _ = exp.DoWithExponentialBackoffAsync("Watch ingresses", action, cancellationToken);
        }
        else
        {
            log.LogInformation("Watching for ingress is disabled");
        }

        return Task.CompletedTask;
    }

    private async Task WatchIngressesAsync(CancellationToken tok)
    {
        log.LogInformation("Watching for ingress changes");
        await watch.WatchIngressesAsync(HandleIngressEventAsync, tok);
    }

    private Task HandleIngressEventAsync(WatchEventType type, V1Ingress ingress)
    {
        try
        {
            log.LogInformation("Ingress change event [{type}] for {ns}-{name}", type, ingress.Namespace(), ingress.Name());
            certChange.RunCheck();
        }
        catch (TaskCanceledException ex)
        {
            log.LogError(ex, "Ingress watch event handler cancelled.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Ingress watch event handler failed unexpectedly");
        }

        return Task.CompletedTask;
    }
}
