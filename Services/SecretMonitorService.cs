using k8s;
using k8s.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class SecretMonitorService : IHostedService
{
    private readonly ILogger<SecretMonitorService> _log;
    private readonly KCertClient _kcert;
    private readonly K8sClient _k8s;
    private readonly CertClient _cert;
    private readonly KCertConfig _cfg;
    private readonly ExponentialBackoff _exp;

    public SecretMonitorService(ILogger<SecretMonitorService> log, KCertClient kcert, K8sClient k8s, CertClient cert, KCertConfig cfg, ExponentialBackoff exp)
    {
        _log = log;
        _kcert = kcert;
        _k8s = k8s;
        _cert = cert;
        _cfg = cfg;
        _exp = exp;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_cfg.WatchIngresses)
        {
            _log.LogInformation("Watching for secrets is enabled");
            var action = () => WatchSecretsAsync(cancellationToken);
            _ = _exp.DoWithExponentialBackoffAsync("Watch secrets", action, cancellationToken);
        }
        else
        {
            _log.LogInformation("Watching for secrets is disabled");
        }

        return Task.CompletedTask;
    }

    private async Task WatchSecretsAsync(CancellationToken cancellationToken)
    {
        _log.LogInformation("Watching for ingress changes");
        await _k8s.WatchSecretsAsync(HandleSecretEventAsync, cancellationToken);
    }

    private async Task HandleSecretEventAsync(WatchEventType type, V1Secret secret, CancellationToken tok)
    {
        try
        {
            _log.LogInformation("Secret change event [{type}] for {ns}-{name}", type, secret.Namespace(), secret.Name());
            if (type != WatchEventType.Added && type != WatchEventType.Modified)
            {
                return;
            }

            await HandleCertificateRequestAsync(secret, tok)
        }
        catch (TaskCanceledException ex)
        {
            _log.LogError(ex, "Secrets event handler cancelled.");
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Secrets event handler failed unexpectedly");
        }
    }

    private async Task HandleCertificateRequestAsync(V1Secret secret, CancellationToken tok)
    {
        var ns = secret.Namespace();
        var name = secret.Name();
        
        if (!secret.Data.TryGetValue("hosts", out var hostBytes))
        {
            _log.LogError("Secret {ns}-{n} does not a hosts entry", ns, name);
            return;
        }

        var hosts = Encoding.UTF8.GetString(hostBytes).Split(',');
        if (hosts.Length < 1)
        {
            _log.LogError("Secret {ns}-{n} does not contain a list of hosts", ns, name);
            return;
        }

        await _kcert.StartRenewalProcessAsync(ns, name, hosts, tok);
    }
}
