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
    private readonly KCertClient _kcert;
    private readonly K8sClient _k8s;
    private readonly K8sWatchClient _watch;
    private readonly CertClient _cert;
    private readonly KCertConfig _cfg;
    private readonly ExponentialBackoff _exp;

    public IngressMonitorService(ILogger<IngressMonitorService> log, KCertClient kcert, K8sClient k8s, CertClient cert, KCertConfig cfg, ExponentialBackoff exp, K8sWatchClient watch)
    {
        _log = log;
        _kcert = kcert;
        _k8s = k8s;
        _cert = cert;
        _cfg = cfg;
        _exp = exp;
        _watch = watch;
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

    private async Task HandleIngressEventAsync(WatchEventType type, V1Ingress ingress, CancellationToken tok)
    {
        try
        {
            _log.LogInformation("Ingress change event [{type}] for {ns}-{name}", type, ingress.Namespace(), ingress.Name());
            if (type != WatchEventType.Added && type != WatchEventType.Modified)
            {
                return;
            }

            // fetch all ingresses to figure out which certs need have which hosts
            var nsLookup = new Dictionary<(string Namespace, string Name), HashSet<string>>();
            await foreach (var ing in _k8s.GetAllIngressesAsync())
            {
                _log.LogInformation("Processing ingress {ns}:{n}", ing.Namespace(), ing.Name());
                foreach (var tls in ing?.Spec?.Tls ?? new List<V1IngressTLS>())
                {
                    _log.LogInformation("Processing secret {s}", tls.SecretName);
                    var key = (ing.Namespace(), tls.SecretName);
                    if (!nsLookup.TryGetValue(key, out var hosts))
                    {
                        hosts = new HashSet<string>();
                        nsLookup.Add(key, hosts);
                    }

                    foreach (var h in tls.Hosts)
                    {
                        hosts.Add(h);
                    }
                }
            }

            foreach (var ((ns, name), hosts) in nsLookup)
            {
                _log.LogInformation("Handling cert {ns} - {name} hosts: {h}", ns, name, string.Join(",", hosts));
                await _kcert.RenewIfNeededAsync(ns, name, hosts.ToArray(), tok);
            }
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "Ingress watch event handler cancelled.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ingress watch event handler failed unexpectedly");
        }
    }
}
