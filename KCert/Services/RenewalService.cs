using k8s.Models;
using KCert.Models;

namespace KCert.Services;

public class RenewalService(ILogger<RenewalService> log, KCertClient kcert, KCertConfig cfg, K8sClient k8s, CertClient cert, EmailClient email) : IHostedService
{
    private const int MaxServiceFailures = 5;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cfg.EnableAutoRenew)
        {
            _ = StartInnerAsync(cancellationToken);
        }

        return Task.CompletedTask;
    }

    public async Task StartInnerAsync(CancellationToken cancellationToken)
    {
        int numFailures = 0;
        while (numFailures < MaxServiceFailures && !cancellationToken.IsCancellationRequested)
        {
            Exception? error = null;
            log.LogInformation("Starting up renewal service.");
            try
            {
                await RunLoopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                if (ex is TaskCanceledException)
                {
                    log.LogError(ex, "Renewal loop cancelled.");
                    throw;
                }

                numFailures++;
                log.LogError(ex, "Renewal Service encountered error {numFailures} of max {MaxServiceFailures}", numFailures, MaxServiceFailures);
                error = ex;
            }

            if (error != null)
            {
                try
                {
                    await email.NotifyFailureAsync("Certificate renewal failed unexpectedly", error, cancellationToken);
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to send cert renewal failure email");
                }
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken tok)
    {
        while (true)
        {
            await StartRenewalJobAsync(tok);
            log.LogInformation("Sleeping for {renewalTime}", cfg.RenewalTimeBetweenChecks);
            await Task.Delay(cfg.RenewalTimeBetweenChecks, tok);
        }
    }

    private async Task StartRenewalJobAsync(CancellationToken tok)
    {
        if (!cfg.EnableAutoRenew)
        {
            return;
        }

        log.LogInformation("Checking for certs that need renewals...");
        await foreach (var secret in k8s.GetManagedSecretsAsync(tok))
        {
            await TryRenewAsync(secret, tok);
        }

        log.LogInformation("Renewal check completed.");
    }

    private async Task TryRenewAsync(V1Secret secret, CancellationToken tok)
    {
        var certVal = cert.GetCert(secret);
        var hosts = cert.GetHosts(certVal);

        if (DateTime.UtcNow < certVal.NotAfter - cfg.RenewalExpirationLimit)
        {
            log.LogInformation("{ns} / {name} / {hosts} doesn't need renewal", secret.Namespace(), secret.Name(), string.Join(',', hosts));
            return;
        }

        log.LogInformation("Renewing: {ns} / {name} / {hosts}", secret.Namespace(), secret.Name(), string.Join(',', hosts));

        try
        {
            await kcert.StartRenewalProcessAsync(secret.Namespace(), secret.Name(), [.. hosts], tok);
            await email.NotifyRenewalResultAsync(secret.Namespace(), secret.Name(), null, tok);
        }
        catch (RenewalException ex)
        {
            await email.NotifyRenewalResultAsync(secret.Namespace(), secret.Name(), ex, tok);
        }
    }
}
