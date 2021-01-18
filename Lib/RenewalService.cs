﻿using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Lib
{
    [Service]
    public class RenewalService : IHostedService
    {
        private const int MaxServiceFailures = 5;

        private readonly K8sClient _k8s;
        private readonly KCertClient _kcert;
        private readonly EmailClient _email;
        private readonly KCertConfig _cfg;
        private readonly ILogger<RenewalService> _log;
        private int _numFailures = 0;

        public RenewalService(ILogger<RenewalService> log, KCertConfig cfg, K8sClient k8s, KCertClient kcert, EmailClient email)
        {
            _cfg = cfg;
            _log = log;
            _k8s = k8s;
            _kcert = kcert;
            _email = email;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            while (_numFailures < MaxServiceFailures && !cancellationToken.IsCancellationRequested)
            {
                _log.LogInformation("Starting up renewal service.");
                try
                {
                    await RunLoopAsync(cancellationToken);
                }
                catch (TaskCanceledException ex)
                {
                    _log.LogInformation($"Renewal loop cancelled. Exiting.");
                    break;
                }
                catch (Exception ex)
                {
                    _numFailures++;
                    _log.LogError(ex, $"Renewal Service encountered error {_numFailures} of max {MaxServiceFailures}");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task RunLoopAsync(CancellationToken tok)
        {
            while(true)
            {
                var p = await _kcert.GetConfigAsync();
                if (p?.EnableAutoRenew ?? false)
                {
                    tok.ThrowIfCancellationRequested();
                    await StartRenewalJobAsync(p, tok);
                }

                await Task.Delay(_cfg.RenewalTimeBetweenChekcs, tok);
            }
        }

        private async Task StartRenewalJobAsync(KCertParams p, CancellationToken tok)
        {
            _log.LogInformation("Checking for ingresses that need renewals...");
            foreach (var ingress in await _k8s.GetAllIngressesAsync())
            {
                tok.ThrowIfCancellationRequested();
                await TryRenewAsync(p, ingress, tok);
            }
            _log.LogInformation("Renewal check completed.");
        }

        private async Task TryRenewAsync(KCertParams p, Networkingv1beta1Ingress ingress, CancellationToken tok)
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

            var cert = secret.Cert();
            if (DateTime.UtcNow < cert.NotAfter - _cfg.RenewalExpirationLimit)
            {
                _log.LogInformation($"{ingress.Namespace()} / {ingress.Name()} / {string.Join(',', ingress.Hosts())} doesn't need renewal");
                return;
            }

            tok.ThrowIfCancellationRequested();
            _log.LogInformation($"Renewing: {ingress.Namespace()} / {ingress.Name()} / {string.Join(',', ingress.Hosts())}");
            var result = await _kcert.GetCertAsync(ingress.Namespace(), ingress.Name());
            await _email.NotifyRenewalResultAsync(p, result);
        }
    }
}