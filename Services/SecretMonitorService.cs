using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services
{
    public class SecretMonitorService : IHostedService
    {
        private readonly ILogger<IngressMonitorService> _log;
        private readonly K8sClient _k8s;
        private readonly KCertConfig _cfg;

        public SecretMonitorService(KCertConfig cfg, ILogger<IngressMonitorService> log, K8sClient k8s)
        {
            _cfg = cfg;
            _log = log;
            _k8s = k8s;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (_cfg.WatchIngresses)
            {
                _ = WatchSecretsAsync(cancellationToken);
            }

            return Task.CompletedTask;
        }

        private async Task WatchSecretsAsync(CancellationToken tok)
        {
            int numTries = 5;
            while (numTries-- > 0)
            {
                try
                {
                    _log.LogInformation("Watching for ingress changes");
                    await _k8s.WatchIngressesAsync((e, i) => HandleIngressEventAsync(e, i, tok), tok);
                }
                catch (Exception ex)
                {
                    if (ex is TaskCanceledException)
                    {
                        _log.LogError(ex, "Ingress watch service cancelled.");
                        throw;
                    }

                    _log.LogError(ex, "Ingress watcher failed");
                    try
                    {
                        await _email.NotifyFailureAsync("Ingress watching failed unexpectedly", ex);
                    }
                    catch (Exception ex2)
                    {
                        _log.LogError(ex2, "Failed to send error notification");
                    }
                }

                _log.LogError("Watch Ingresses failed. Sleeping for 10 seconds then trying {n} more times.", numTries);
                await Task.Delay(TimeSpan.FromSeconds(10), tok);
            }
        }
    }
}
