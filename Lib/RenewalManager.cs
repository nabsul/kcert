using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KCert.Lib
{
    public class RenewalManager
    {
        private readonly K8sClient _k8s;
        private readonly GetCertHandler _renew;
        private readonly KCertClient _kcert;
        private readonly IConfiguration _cfg;
        private readonly ILogger<RenewalManager> _log;
        private readonly Task _job;

        public RenewalManager(ILogger<RenewalManager> log, IConfiguration cfg, K8sClient k8s, GetCertHandler renew, KCertClient kcert)
        {
            _cfg = cfg;
            _log = log;
            _job = StartRenewalJobAsync();
            _k8s = k8s;
            _renew = renew;
            _kcert = kcert;
        }


        private async Task StartRenewalJobAsync()
        {
            if (!_cfg.GetValue<bool>("Renewal:enabled"))
            {
                _log.LogInformation("Existing because renewal is disabled.");
                return;
            }

            try
            {
                while (true)
                {
                    _log.LogInformation("Checking for ingresses that need renewals...");
                    await StartRenewalJobInnerAsync();
                    _log.LogInformation("Renewal check completed.");
                    var delay = TimeSpan.FromHours(_cfg.GetValue<int>("renewal:HoursBetweenChecks"));
                    await Task.Delay(delay);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Renewal manager job failed: {ex.Message}");
            }
        }

        private async Task StartRenewalJobInnerAsync()
        {
            try
            {
                foreach (var ingress in await _k8s.GetAllIngressesAsync(""))
                {
                    await TryRenewAsync(ingress);
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Renewal job failed: {ex.Message}");
            }
        }

        private async Task TryRenewAsync(Networkingv1beta1Ingress ingress)
        {
            var ns = ingress.Namespace();
            var secretName = ingress.SecretName();
            if (secretName == null)
            {
                return;
            }

            var secret = await _k8s.GetSecretAsync(ns, secretName);
            if (secret == null)
            {
                return;
            }

            await _kcert.GetCertAsync(ingress.Name());
        }
    }
}
