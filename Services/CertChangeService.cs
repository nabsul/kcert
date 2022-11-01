using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using k8s.Models;
using Microsoft.Extensions.Logging;

namespace KCert.Services;

[Service]
public class CertChangeService
{
    private readonly ILogger<CertChangeService> _log;
    private readonly K8sClient _k8s;
    private readonly KCertClient _kcert;

    private Task _runningTask = Task.CompletedTask;
    private Task _nextTask = null;

    public CertChangeService(ILogger<CertChangeService> log, K8sClient k8s, KCertClient kcert)
    {
        _log = log;
        _k8s = k8s;
        _kcert = kcert;
    }

    // Ensures that at least one whole check for changes is executed after every call to this function
    // In otherwords:
    // - If no check is currently running, it will kick off a check
    // - If a check is already running, it will queue up another one to run after the current one completes
    // - However, it will not queue up multiple checks
    public void RunCheck()
    {
        lock(this)
        {
            if (_nextTask != null)
            {
                return;
            }

            _nextTask = CheckForChangesAsync(_runningTask);
        }
    }

    private async Task CheckForChangesAsync(Task previous)
    {
        await previous;

        lock(this)
        {
            _runningTask = _nextTask;
            _nextTask = null;
        }

        // fetch all ingresses to figure out which certs need have which hosts
        var nsLookup = new Dictionary<(string Namespace, string Name), HashSet<string>>();
        await foreach (var (ns, name, hosts) in MergeAsync(GetIngressCertsAsync(), GetConfigMapCertsAsync()))
        {
            var key = (ns, name);
            if (!nsLookup.TryGetValue(key, out var currHosts))
            {
                currHosts = new HashSet<string>();
                nsLookup.Add(key, currHosts);
            }

            foreach (var h in hosts)
            {
                currHosts.Add(h);
            }
        }

        foreach (var ((ns, name), hosts) in nsLookup)
        {
            _log.LogInformation("Handling cert {ns} - {name} hosts: {h}", ns, name, string.Join(",", hosts));
            await _kcert.RenewIfNeededAsync(ns, name, hosts.ToArray(), CancellationToken.None);
        }
    }

    private static async IAsyncEnumerable<T> MergeAsync<T>(params IAsyncEnumerable<T>[] enumerators)
    {
        foreach (var e in enumerators)
        {
            await foreach (var v in e)
            {
                yield return v;
            }
        }
    }

    private async IAsyncEnumerable<(string Namespace, string Name, IEnumerable<string> hosts)> GetIngressCertsAsync()
    {
        await foreach (var ing in _k8s.GetAllIngressesAsync())
        {
            _log.LogInformation("Processing ingress {ns}:{n}", ing.Namespace(), ing.Name());
            foreach (var tls in ing?.Spec?.Tls ?? new List<V1IngressTLS>())
            {
                _log.LogInformation("Processing secret {s}", tls.SecretName);
                yield return (ing.Namespace(), tls.SecretName, tls.Hosts);
            }
        }
    }

    private async IAsyncEnumerable<(string Namespace, string Name, IEnumerable<string> hosts)> GetConfigMapCertsAsync()
    {
        await foreach (var config in _k8s.GetAllConfigMapsAsync())
        {
            _log.LogInformation("Processing configmap {ns}:{n}", config.Namespace(), config.Name());
            var ns = config.Namespace();
            var name = config.Name();
            
            if (!config.Data.TryGetValue("hosts", out var hostList))
            {
                _log.LogError("ConfigMap {ns}-{n} does not have a hosts entry", ns, name);
                continue;
            }

            var hosts = hostList.Split(',');
            if (hosts.Length < 1)
            {
                _log.LogError("ConfigMap {ns}-{n} does not contain a list of hosts", ns, name);
                continue;
            }

            yield return (ns, name, hosts);
        }
    }
}
