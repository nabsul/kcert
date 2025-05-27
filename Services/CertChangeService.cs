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
        log.LogInformation("CheckForChangesAsync: Waiting for semaphore.");

        await _sem.WaitAsync();
        if (_lastRun > start)
        {
            log.LogInformation("CheckForChangesAsync: Check already queued or recently completed after this request. No need to run this instance.");
            _sem.Release();
            return;
        }

        _lastRun = start;
        log.LogInformation("CheckForChangesAsync: Starting check for certificate changes.");

        try
        {
            var ingressCerts = new List<(string Namespace, string Name, IEnumerable<string> hosts)>();
            await foreach (var cert in GetIngressCertsAsync())
            {
                ingressCerts.Add(cert);
            }
            log.LogInformation("CheckForChangesAsync: Found {Count} certificate definitions from Ingresses.", ingressCerts.Count);

            var configMapCerts = new List<(string Namespace, string Name, IEnumerable<string> hosts)>();
            await foreach (var cert in GetConfigMapCertsAsync())
            {
                configMapCerts.Add(cert);
            }
            log.LogInformation("CheckForChangesAsync: Found {Count} certificate definitions from ConfigMaps.", configMapCerts.Count);

            var nsLookup = new Dictionary<(string Namespace, string Name), HashSet<string>>();
            foreach (var (ns, name, hosts) in ingressCerts.Concat(configMapCerts))
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

            log.LogInformation("CheckForChangesAsync: Total {Count} unique certificate definitions to process after merging Ingress and ConfigMap sources.", nsLookup.Count);
            foreach (var ((ns, name), hosts) in nsLookup)
            {
                log.LogInformation("CheckForChangesAsync: Queued for processing: Secret '{Namespace}/{SecretName}', Hosts: [{HostList}]", ns, name, string.Join(", ", hosts));
            }

            foreach (var ((ns, name), hosts) in nsLookup)
            {
                log.LogInformation("Handling cert {ns} - {name} hosts: {h}", ns, name, string.Join(",", hosts)); // Existing log, good as is.
                await kcert.RenewIfNeededAsync(ns, name, hosts.ToArray(), CancellationToken.None);
            }

            log.LogInformation("CheckForChangesAsync: Check for certificate changes completed.");
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
        // This method's logging seems fine based on the prompt, focusing on GetConfigMapCertsAsync and CheckForChangesAsync.
        // For consistency, I'll ensure its messages are clear.
        log.LogInformation("GetIngressCertsAsync: Starting to fetch and process Ingresses for certificate definitions.");
        int ingressCount = 0;
        await foreach (var ing in k8s.GetAllIngressesAsync())
        {
            ingressCount++;
            log.LogInformation("GetIngressCertsAsync: Processing Ingress {Namespace}/{Name}", ing.Namespace(), ing.Name());
            if (ing?.Spec?.Tls == null || !ing.Spec.Tls.Any())
            {
                log.LogInformation("GetIngressCertsAsync: Ingress {Namespace}/{Name} has no TLS specifications, skipping.", ing.Namespace(), ing.Name());
                continue;
            }
            foreach (var tls in ing.Spec.Tls)
            {
                log.LogInformation("GetIngressCertsAsync: Ingress {Namespace}/{Name} - found Secret {SecretName} for hosts [{HostList}]", ing.Namespace(), ing.Name(), tls.SecretName, string.Join(", ", tls.Hosts));
                yield return (ing.Namespace(), tls.SecretName, tls.Hosts);
            }
        }
        log.LogInformation("GetIngressCertsAsync: Processed {Count} Ingresses.", ingressCount);
    }

    private async IAsyncEnumerable<(string Namespace, string Name, IEnumerable<string> hosts)> GetConfigMapCertsAsync()
    {
        log.LogInformation("GetConfigMapCertsAsync: Starting to fetch and process ConfigMaps for certificate definitions.");
        int foundConfigMapsCount = 0;
        await foreach (var config in k8s.GetAllConfigMapsAsync())
        {
            foundConfigMapsCount++;
            log.LogInformation("GetConfigMapCertsAsync: Processing ConfigMap {Namespace}/{Name}", config.Namespace(), config.Name());
            var ns = config.Namespace();
            var name = config.Name();

            if (config.Data == null)
            {
                log.LogWarning("GetConfigMapCertsAsync: ConfigMap {Namespace}/{Name} has no Data section, skipping.", ns, name);
                continue;
            }

            if (!config.Data.TryGetValue("hosts", out var hostList))
            {
                log.LogWarning("GetConfigMapCertsAsync: ConfigMap {Namespace}/{Name} does not have a 'hosts' key in Data, skipping.", ns, name);
                continue;
            }
            
            log.LogInformation("GetConfigMapCertsAsync: ConfigMap {Namespace}/{Name} found 'hosts' key. Raw value: '{HostListValue}'", ns, name, hostList);

            var hosts = hostList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (hosts.Length < 1)
            {
                log.LogWarning("GetConfigMapCertsAsync: ConfigMap {Namespace}/{Name} 'hosts' key is empty or invalid after splitting, skipping.", ns, name);
                continue;
            }

            log.LogInformation("GetConfigMapCertsAsync: Yielding cert definition from ConfigMap {Namespace}/{Name} for hosts: [{HostList}]", ns, name, string.Join(", ", hosts));
            yield return (ns, name, hosts);
        }
        log.LogInformation("GetConfigMapCertsAsync: Found {count} ConfigMaps after filtering by K8sClient.", foundConfigMapsCount);
    }
}
