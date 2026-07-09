using k8s.Autorest;
using KCert.Models;

namespace KCert.Services;

public class KCertClient(K8sClient kube, ILogger<KCertClient> log, EmailClient email, CertClient cert, IServiceProvider svc)
{
    private readonly SemaphoreSlim _semaphore = new(1);

    public async Task RenewIfNeededAsync(string ns, string name, string[] hosts, CancellationToken tok)
    {
        var secret = await kube.GetSecretAsync(ns, name, tok);
        if (false == await IsRenewalNeededAsync(ns, name, hosts, tok))
        {
            // nothing to do, cert already has all the hosts it needs to have
            log.LogInformation("Certificate already has all the needed hosts configured");
            return;
        }

        await StartRenewalProcessAsync(ns, name, hosts, tok);   
    }

    private async Task<bool> IsRenewalNeededAsync(string ns, string name, string[] hosts, CancellationToken tok)
    {
        var secret = await kube.GetSecretAsync(ns, name, tok);
        if (secret == null) return true;
        var currentHosts = cert.GetHosts(cert.GetCert(secret));
        return hosts.Union(currentHosts).Count() != hosts.Length;
    }

    public async Task StartRenewalProcessAsync(string ns, string secretName, string[] hosts, CancellationToken tok)
    {
        await _semaphore.WaitAsync(tok);
        try
        {
            log.LogInformation("Starting renewal process for secret {ns}/{secretName} with hosts {hosts}", ns, secretName, string.Join(", ", hosts));
            await RenewCertAsync(ns, secretName, hosts, tok);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task RenewCertAsync(string ns, string secretName, string[] hosts, CancellationToken tok)
    {
        var getCert = svc.GetRequiredService<RenewalHandler>();
        try
        {
            await getCert.RenewCertAsync(ns, secretName, hosts, tok);
            await email.NotifyRenewalResultAsync(ns, secretName, null, tok);
        }
        catch (RenewalException ex)
        {
            log.LogError(ex, "Renewal failed");
            await email.NotifyRenewalResultAsync(ns, secretName, ex, tok);
        }
        catch (HttpOperationException ex)
        {
            log.LogError(ex, "HTTP Operation failed with response: {resp}", ex.Response.Content);
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Unexpected renewal failure");
        }
    }
}
