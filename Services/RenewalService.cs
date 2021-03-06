using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services
{
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

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            int numFailures = 0;
            while (numFailures < MaxServiceFailures && !cancellationToken.IsCancellationRequested)
            {
                _log.LogInformation("Starting up renewal service.");
                try
                {
                    await RunLoopAsync(cancellationToken);
                }
                catch (TaskCanceledException ex)
                {
                    _log.LogError(ex, "Renewal loop cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    numFailures++;
                    _log.LogError(ex, $"Renewal Service encountered error {numFailures} of max {MaxServiceFailures}");
                }
            }
        }

        private async Task RunLoopAsync(CancellationToken tok)
        {
            while (true)
            {
                tok.ThrowIfCancellationRequested();
                await StartRenewalJobAsync(tok);
                await Task.Delay(_cfg.RenewalTimeBetweenChekcs, tok);
            }
        }

        private async Task StartRenewalJobAsync(CancellationToken tok)
        {
            var p = await _kcert.GetConfigAsync();
            if (!(p?.EnableAutoRenew ?? false))
            {
                return;
            }

            _log.LogInformation("Checking for ingresses that need renewals...");
            foreach (var ingress in await _k8s.GetAllIngressesAsync())
            {
                foreach (var tls in ingress.Spec.Tls)
                {
                    tok.ThrowIfCancellationRequested();
                    await TryRenewAsync(p, ingress, tls.SecretName, tok);
                }
            }
            
            _log.LogInformation("Renewal check completed.");
        }

        private async Task TryRenewAsync(KCertParams p, V1Ingress ingress, string secretName, CancellationToken tok)
        {
            var hosts = ingress.Spec.Rules.Select(r => r.Host);

            if (!await NeedsRenewalAsync(ingress, secretName))
            {
                _log.LogInformation($"{ingress.Namespace()} / {ingress.Name()} / {string.Join(',', hosts)} doesn't need renewal");
                return;
            }

            tok.ThrowIfCancellationRequested();
            _log.LogInformation($"Renewing: {ingress.Namespace()} / {ingress.Name()} / {string.Join(',', hosts)}");

            try
            {
                await _kcert.RenewCertAsync(ingress.Namespace(), ingress.Name());
                await _email.NotifyRenewalResultAsync(p, ingress.Namespace(), ingress.Name(), null);
            }
            catch (RenewalException ex)
            {
                await _email.NotifyRenewalResultAsync(p, ingress.Namespace(), ingress.Name(), ex);
            }
        }

        private async Task<bool> NeedsRenewalAsync(V1Ingress ingress, string secretName)
        {
            var ns = ingress.Namespace();
            if (secretName == null)
            {
                return false;
            }

            var secret = await _k8s.GetSecretAsync(ns, secretName);
            if (secret == null)
            {
                return false;
            }

            var c = _cert.GetCert(secret);
            if (DateTime.UtcNow < c.NotAfter - _cfg.RenewalExpirationLimit)
            {
                return false;
            }

            return true;
        }
    }
}
