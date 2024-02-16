using k8s.Models;

namespace KCert.Services;

[Service]
public class CertChangeService(ILogger<CertChangeService> log, K8sClient k8s, KCertClient kcert)
{
    private DateTime _lastRun = DateTime.MinValue;

    private SemaphoreSlim _sem = new(1, 1);

    // Ensures that at least one whole check for changes is executed after every call to this function
    // In otherwords:
    // - If no check is currently running, it will kick off a check
    // - If a check is already running, it will queue up another one to run after the current one completes
    // - However, it will not queue up multiple checks
    public void RunCheck()
    {
        _ = CheckForChangesAsync();
    }

    private async Task CheckForChangesAsync()
    {
        var start = DateTime.UtcNow;
        log.LogInformation("Waiting for semaphore");

        await _sem.WaitAsync();
        if (_lastRun > start)
        {
            log.LogInformation("No need to run this check");
            _sem.Release();
            return;
        }

        _lastRun = start;
        log.LogInformation("Starting check for changes.");

        try
        {
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
                log.LogInformation("Handling cert {ns} - {name} hosts: {h}", ns, name, string.Join(",", hosts));
                await kcert.RenewIfNeededAsync(ns, name, hosts.ToArray(), CancellationToken.None);
            }

            log.LogInformation("Check for changes completed.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to check for cert changes.");
        }
        finally
        {
            _sem.Release();
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
        await foreach (var ing in k8s.GetAllIngressesAsync())
        {
            log.LogInformation("Processing ingress {ns}:{n}", ing.Namespace(), ing.Name());
            foreach (var tls in ing?.Spec?.Tls ?? new List<V1IngressTLS>())
            {
                log.LogInformation("Processing secret {s}", tls.SecretName);
                yield return (ing.Namespace(), tls.SecretName, tls.Hosts);
            }
        }
    }

    private async IAsyncEnumerable<(string Namespace, string Name, IEnumerable<string> hosts)> GetConfigMapCertsAsync()
    {
        await foreach (var config in k8s.GetAllConfigMapsAsync())
        {
            log.LogInformation("Processing configmap {ns}:{n}", config.Namespace(), config.Name());
            var ns = config.Namespace();
            var name = config.Name();

            if (!config.Data.TryGetValue("hosts", out var hostList))
            {
                log.LogError("ConfigMap {ns}-{n} does not have a hosts entry", ns, name);
                continue;
            }

            var hosts = hostList.Split(',');
            if (hosts.Length < 1)
            {
                log.LogError("ConfigMap {ns}-{n} does not contain a list of hosts", ns, name);
                continue;
            }

            yield return (ns, name, hosts);
        }
    }
}
