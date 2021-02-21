using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly IServiceProvider _services;

        public RenewalService(IServiceProvider services)
        {
            _services = services;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // The IHosted service is created as a singleton, but all other services are scoped
            // For this reason, we have to create a scope and manually fetch the services
            using var scope = _services.CreateScope();
            var log = scope.ServiceProvider.GetService<ILogger<RenewalService>>();

            int numFailures = 0;
            while (numFailures < MaxServiceFailures && !cancellationToken.IsCancellationRequested)
            {
                log.LogInformation("Starting up renewal service.");
                try
                {
                    await RunLoopAsync(scope, cancellationToken);
                }
                catch (TaskCanceledException ex)
                {
                    log.LogError(ex, "Renewal loop cancelled.");
                    break;
                }
                catch (Exception ex)
                {
                    numFailures++;
                    log.LogError(ex, $"Renewal Service encountered error {numFailures} of max {MaxServiceFailures}");
                }
            }
        }

        private async Task RunLoopAsync(IServiceScope scope, CancellationToken tok)
        {
            var cfg = scope.ServiceProvider.GetService<KCertConfig>();

            while (true)
            {
                tok.ThrowIfCancellationRequested();
                await StartRenewalJobAsync(scope, tok);
                await Task.Delay(cfg.RenewalTimeBetweenChekcs, tok);
            }
        }

        private async Task StartRenewalJobAsync(IServiceScope scope, CancellationToken tok)
        {
            var log = scope.ServiceProvider.GetService<ILogger<RenewalService>>();
            var k8s = scope.ServiceProvider.GetService<K8sClient>();
            var kcert = scope.ServiceProvider.GetService<KCertClient>();

            var p = await kcert.GetConfigAsync();
            if (!(p?.EnableAutoRenew ?? false))
            {
                return;
            }

            log.LogInformation("Checking for ingresses that need renewals...");
            foreach (var ingress in await k8s.GetAllIngressesAsync())
            {
                foreach (var tls in ingress.Spec.Tls)
                {
                    tok.ThrowIfCancellationRequested();
                    await TryRenewAsync(scope, p, ingress, tls.SecretName, tok);
                }
            }
            log.LogInformation("Renewal check completed.");
        }

        private async Task TryRenewAsync(IServiceScope scope, KCertParams p, V1Ingress ingress, string secretName, CancellationToken tok)
        {
            var log = scope.ServiceProvider.GetService<ILogger<RenewalService>>();
            var kcert = scope.ServiceProvider.GetService<KCertClient>();
            var email = scope.ServiceProvider.GetService<EmailClient>();

            var hosts = ingress.Spec.Rules.Select(r => r.Host);

            if (!await NeedsRenewalAsync(scope, ingress, secretName))
            {
                log.LogInformation($"{ingress.Namespace()} / {ingress.Name()} / {string.Join(',', hosts)} doesn't need renewal");
                return;
            }

            tok.ThrowIfCancellationRequested();
            log.LogInformation($"Renewing: {ingress.Namespace()} / {ingress.Name()} / {string.Join(',', hosts)}");

            try
            {
                await kcert.GetCertAsync(ingress.Namespace(), ingress.Name());
                await email.NotifyRenewalResultAsync(p, ingress.Namespace(), ingress.Name(), null);
            }
            catch (RenewalException ex)
            {
                await email.NotifyRenewalResultAsync(p, ingress.Namespace(), ingress.Name(), ex);
            }
        }

        private async Task<bool> NeedsRenewalAsync(IServiceScope scope, V1Ingress ingress, string secretName)
        {
            var k8s = scope.ServiceProvider.GetService<K8sClient>();
            var cert = scope.ServiceProvider.GetService<CertClient>();
            var cfg = scope.ServiceProvider.GetService<KCertConfig>();

            var ns = ingress.Namespace();
            if (secretName == null)
            {
                return false;
            }

            var secret = await k8s.GetSecretAsync(ns, secretName);
            if (secret == null)
            {
                return false;
            }

            var c = cert.GetCert(secret);
            if (DateTime.UtcNow < c.NotAfter - cfg.RenewalExpirationLimit)
            {
                return false;
            }

            return true;
        }
    }
}
