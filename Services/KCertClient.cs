using k8s.Autorest;
using KCert.Models;

namespace KCert.Services;

[Service]
public class KCertClient(K8sClient kube, RenewalHandler getCert, ILogger<KCertClient> log, EmailClient email, CertClient cert)
{
    private Task _running = Task.CompletedTask;

    public async Task RenewIfNeededAsync(string ns, string name, string[] hosts, CancellationToken tok)
    {
        var secret = await kube.GetSecretAsync(ns, name);
        tok.ThrowIfCancellationRequested();

        if (secret != null)
        {
            var c = cert.GetCert(secret);
            var certHosts = cert.GetHosts(c).ToHashSet();
            if (hosts.Length == certHosts.Count && hosts.All(h => certHosts.Contains(h)))
            {
                // nothing to do, cert already has all the hosts it needs to have
                log.LogInformation("Certificate already has all the needed hosts configured");
                return;
            }
        }

        await StartRenewalProcessAsync(ns, name, hosts, tok);
    }

    // Ensure that no certs are renewed in parallel
    public Task StartRenewalProcessAsync(string ns, string secretName, string[] hosts, CancellationToken tok)
    {
        Task task;
        lock (this)
        {
            task = RenewCertAsync(_running, ns, secretName, hosts, tok);
            _running = task;
        }

        return task;
    }

    private async Task RenewCertAsync(Task prev, string ns, string secretName, string[] hosts, CancellationToken tok)
    {
        try
        {
            await prev;
            tok.ThrowIfCancellationRequested();
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Previous task in rewal chain failed.");
        }

        try
        {
            await getCert.RenewCertAsync(ns, secretName, hosts, tok);
            tok.ThrowIfCancellationRequested();

            await email.NotifyRenewalResultAsync(ns, secretName, null);
            tok.ThrowIfCancellationRequested();
        }
        catch (RenewalException ex)
        {
            log.LogError(ex, "Renewal failed");
            await email.NotifyRenewalResultAsync(ns, secretName, ex);
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
