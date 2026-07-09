using System.Runtime.CompilerServices;
using k8s.Models;

namespace KCert.Services;

public class CertChangeService(ILogger<CertChangeService> log, K8sClient k8s, KCertClient kcert)
{
    private DateTime _lastRun = DateTime.MinValue;

    private readonly SemaphoreSlim _sem = new(1, 1);

    // Ensures that at least one whole check for changes is executed after every call to this function
    // In otherwords:
    // - If no check is currently running, it will kick off a check
    // - If a check is already running, it will queue up another one to run after the current one completes
    // - However, it will not queue up multiple checks
    public void RunCheck(CancellationToken tok)
    {
        _ = CheckForChangesAsync(tok);
    }

    private async Task CheckForChangesAsync(CancellationToken tok)
    {
        var start = DateTime.UtcNow;
        log.LogInformation("CheckForChangesAsync: Waiting for semaphore.");

        await _sem.WaitAsync(tok);
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
            var nsLookup = new Dictionary<(string Namespace, string Name), HashSet<string>>();
            await foreach (var (ns, name, hosts) in GetIngressCertsAsync(tok).Concat(GetConfigMapCertsAsync(tok)))
            {
                var key = (ns, name);
                if (!nsLookup.TryGetValue(key, out var currHosts))
                {
                    currHosts = [];
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
                log.LogInformation("Handling cert {ns} - {name} hosts: {h}", ns, name, string.Join(",", hosts)); // Existing log, good as is.
                await kcert.RenewIfNeededAsync(ns, name, [.. hosts], tok);
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

    private async IAsyncEnumerable<(string Namespace, string Name, IEnumerable<string> Hosts)> GetIngressCertsAsync([EnumeratorCancellation] CancellationToken tok)
    {
        int ingressCount = 0;
        int skipped = 0;
        int hosts = 0;
        await foreach (var ing in k8s.GetAllIngressesAsync(tok))
        {
            ingressCount++;
            if (ing?.Spec?.Tls == null || !ing.Spec.Tls.Any())
            {
                skipped++;
                continue;
            }

            foreach (var tls in ing.Spec.Tls)
            {
                hosts++;
                yield return (ing.Namespace(), tls.SecretName, tls.Hosts);
            }
        }

        log.LogInformation("GetIngressCertsAsync: Processed {Count} Ingresses. Skipped {skipped} ingresses. Extracted {numHosts} hosts", ingressCount, skipped, hosts);
    }

    private async IAsyncEnumerable<(string Namespace, string Name, IEnumerable<string> hosts)> GetConfigMapCertsAsync([EnumeratorCancellation] CancellationToken tok)
    {
        int foundConfigMapsCount = 0;
        int skipped = 0;
        int numHosts = 0;
        await foreach (var config in k8s.GetAllConfigMapsAsync(tok))
        {
            foundConfigMapsCount++;
            var ns = config.Namespace();
            var name = config.Name();

            if (config.Data == null || !config.Data.TryGetValue("hosts", out var hostList))
            {
                skipped++;
                continue;
            }

            var hosts = hostList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

            if (hosts.Length == 0)
            {
                skipped++;
                continue;
            }

            numHosts += hosts.Length;
            yield return (ns, name, hosts);
        }

        log.LogInformation("GetConfigMapCertsAsync: Processed {count} ConfigMaps. Skipped {skipepd}. Extracted {numHosts} hosts.", foundConfigMapsCount, skipped, numHosts);
    }
}
