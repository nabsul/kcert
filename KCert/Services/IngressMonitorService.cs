using k8s;
using k8s.Models;
using Polly;
using Polly.Retry;

namespace KCert.Services;

public class IngressMonitorService(ILogger<IngressMonitorService> log, KCertConfig cfg, K8sWatchClient watch, CertChangeService certChange) : IHostedService
{
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cfg.WatchIngresses)
        {
            log.LogInformation("Watching for ingress is enabled");
            Task action() => WatchIngressesAsync(cancellationToken);
            _ = Policy.Handle<Exception>()
                .WaitAndRetryAsync(5, i => TimeSpan.FromSeconds(Math.Pow(2, i)))
                .ExecuteAsync(async ct => await action(), cancellationToken);
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

    private Task HandleIngressEventAsync(WatchEventType type, V1Ingress ingress, CancellationToken tok)
    {
        try
        {
            log.LogInformation("Ingress change event [{type}] for {ns}-{name}", type, ingress.Namespace(), ingress.Name());
            certChange.RunCheck(tok);
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
