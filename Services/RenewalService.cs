using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class RenewalService : IHostedService
{
    private const int MaxServiceFailures = 5;

    private readonly ILogger<RenewalService> _log;
    private readonly KCertClient _kcert;
    private readonly KCertConfig _cfg;
    private readonly K8sClient _k8s;
    private readonly CertClient _cert;
    private readonly EmailClient _email;

    public RenewalService(ILogger<RenewalService> log, KCertClient kcert, KCertConfig cfg, K8sClient k8s, CertClient cert, EmailClient email)
    {
        _log = log;
        _kcert = kcert;
        _cfg = cfg;
        _k8s = k8s;
        _cert = cert;
        _email = email;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cfg.EnableAutoRenew)
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
            Exception error = null;
            _log.LogInformation("Starting up renewal service.");
            try
            {
                await RunLoopAsync(cancellationToken);
            }
            catch (TaskCanceledException ex)
            {
                _log.LogError(ex, "Renewal loop cancelled.");
            }
            catch (Exception ex)
            {
                numFailures++;
                _log.LogError(ex, "Renewal Service encountered error {numFailures} of max {MaxServiceFailures}", numFailures, MaxServiceFailures);
                error = ex;
            }

            if (error != null)
            {
                try
                {
                    await _email.NotifyFailureAsync("Certificate renewal failed unexpectedly", error);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "Failed to send cert renewal failure email");
                }
            }
        }
    }

    private async Task RunLoopAsync(CancellationToken tok)
    {
        while (true)
        {
            tok.ThrowIfCancellationRequested();
            await StartRenewalJobAsync(tok);
            _log.LogInformation("Sleeping for {renewalTime}", _cfg.RenewalTimeBetweenChecks);
            await Task.Delay(_cfg.RenewalTimeBetweenChecks, tok);
        }
    }

    private async Task StartRenewalJobAsync(CancellationToken tok)
    {
        if (!_cfg.EnableAutoRenew)
        {
            return;
        }

        _log.LogInformation("Checking for certs that need renewals...");
        foreach (var secret in await _k8s.GetManagedSecretsAsync())
        {
            tok.ThrowIfCancellationRequested();
            await TryRenewAsync(secret, tok);
        }

        _log.LogInformation("Renewal check completed.");
    }

    private async Task TryRenewAsync(V1Secret secret, CancellationToken tok)
    {
        var cert = _cert.GetCert(secret);
        var hosts = _cert.GetHosts(cert);

        if (DateTime.UtcNow < cert.NotAfter - _cfg.RenewalExpirationLimit)
        {
            _log.LogInformation("{ns} / {name} / {hosts} doesn't need renewal", secret.Namespace(), secret.Name(), string.Join(',', hosts));
            return;
        }

        tok.ThrowIfCancellationRequested();
        _log.LogInformation("Renewing: {ns} / {name} / {hosts}", secret.Namespace(), secret.Name(), string.Join(',', hosts));

        try
        {
            await _kcert.StartRenewalProcessAsync(secret.Namespace(), secret.Name(), hosts.ToArray(), tok);
            await _email.NotifyRenewalResultAsync(secret.Namespace(), secret.Name(), null);
        }
        catch (RenewalException ex)
        {
            await _email.NotifyRenewalResultAsync(secret.Namespace(), secret.Name(), ex);
        }
    }
}
