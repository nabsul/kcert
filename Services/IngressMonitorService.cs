﻿using k8s;
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
    private readonly CertClient _cert;
    private readonly EmailClient _email;
    private readonly KCertConfig _cfg;

    public IngressMonitorService(ILogger<IngressMonitorService> log, KCertClient kcert, K8sClient k8s, CertClient cert, EmailClient email, KCertConfig cfg)
    {
        _log = log;
        _kcert = kcert;
        _k8s = k8s;
        _cert = cert;
        _email = email;
        _cfg = cfg;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cfg.WatchIngresses)
        {
            _ = WatchIngressesAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    private async Task WatchIngressesAsync(CancellationToken tok)
    {
        int numTries = 5;
        while (numTries-- > 0)
        {
            try
            {
                _log.LogInformation("Watching for ingress changes");
                await _k8s.WatchIngressesAsync(HandleIngressEventAsync, tok);
            }
            catch (TaskCanceledException ex)
            {
                _log.LogError(ex, "Ingress watch service cancelled.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Ingress watcher failed");
                try
                {
                    await _email.NotifyFailureAsync("Ingress watching failed unexpectedly", ex);
                }
                catch (Exception ex2)
                {
                    _log.LogError(ex2, "Failed to send error notification");
                }
            }

            _log.LogError("Watch Ingresses failed. Sleeping for 10 seconds then trying {n} more times.", numTries);
            await Task.Delay(TimeSpan.FromSeconds(10), tok);
        }
    }

    private async Task HandleIngressEventAsync(WatchEventType type, V1Ingress ingress, CancellationToken tok)
    {
        try
        {
            _log.LogInformation("event [{type}] for {ns}-{name}", type, ingress.Namespace(), ingress.Name());
            if (type != WatchEventType.Added && type != WatchEventType.Modified)
            {
                return;
            }

            // fetch all ingresses to figure out which certs need have which hosts
            var nsLookup = new Dictionary<(string, string), HashSet<string>>();
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
                await TryUpdateSecretAsync(ns, name, hosts, tok);
            }
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "Ingress watch service cancelled.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ingress event handler failed unexpectedly");
            try
            {
                await _email.NotifyFailureAsync("Ingress watching failed unexpectedly", ex);
            }
            catch (Exception ex2)
            {
                _log.LogError(ex2, "Failed to send error notification");
            }
        }
    }

    private async Task TryUpdateSecretAsync(string ns, string name, IEnumerable<string> hosts, CancellationToken tok)
    {
        var secret = await _k8s.GetSecretAsync(ns, name);
        tok.ThrowIfCancellationRequested();

        if (secret != null)
        {
            var cert = _cert.GetCert(secret);
            var certHosts = _cert.GetHosts(cert).ToHashSet();
            if (hosts.All(h => certHosts.Contains(h)))
            {
                // nothing to do, cert already has all the hosts it needs to have
                _log.LogInformation("Certificate already has all the needed hosts configured");
                return;
            }
        }

        await _kcert.StartRenewalProcessAsync(ns, name, hosts.ToArray(), tok);
    }
}
