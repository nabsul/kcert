using k8s;
using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
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

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        int numFailures = 0;
        while (numFailures < MaxServiceFailures && !cancellationToken.IsCancellationRequested)
        {
            _log.LogInformation("Starting up renewal service.");
            try
            {
                _ = WatchIngressesAsync(cancellationToken);
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
                _log.LogError(ex, "Renewal Service encountered error {numFailures} of max {MaxServiceFailures}", numFailures, MaxServiceFailures);
            }
        }
    }

    private async Task WatchIngressesAsync(CancellationToken tok)
    {
        try
        {
            _log.LogInformation("Watching for ingress changes");
            await _k8s.WatchIngressesAsync(HandleIngressEventAsync, tok);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ingress watcher failed");
        }
    }

    private async Task HandleIngressEventAsync(WatchEventType type, V1Ingress ingress, CancellationToken tok)
    {
        _log.LogInformation("event [{type}] for {ns}-{name}", type, ingress.Namespace(), ingress.Name());
        if (type != WatchEventType.Added && type != WatchEventType.Modified)
        {
            return;
        }

        // fetch all ingresses to figure out which certs need have which hosts
        var nsLookup = new Dictionary<(string, string), HashSet<string>>();
        await foreach (var ing in _k8s.GetAllIngressesAsync())
        {
            _log.LogInformation("Processing ingress {ns}:{n}", ing.Namespace(), ing.Name());
            foreach (var tls in ing?.Spec?.Tls ?? new List<V1IngressTLS>())
            {
                var key = (ing.Namespace(), tls.SecretName);
                if (!nsLookup.TryGetValue(key, out var hosts))
                {
                    hosts = new HashSet<string>();
                    nsLookup.Add(key, hosts);
                }

                foreach (var h in tls.Hosts)
                {
                    hosts.Add(h);
                }
            }
        }

        foreach (var ((ns, name), hosts) in nsLookup)
        {
            _log.LogInformation("Handling cert {ns} - {name} hosts: {h}", ns, name, string.Join(", ", hosts));
            await TryUpdateSecretAsync(ns, name, hosts, tok);
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

    private async Task TryUpdateSecretAsync(string ns, string name, IEnumerable<string> hosts, CancellationToken tok)
    {
        var secret = await _k8s.GetSecretAsync(ns, name);

        if (secret != null)
        {
            var cert = _cert.GetCert(secret);
            var certHosts = _cert.GetHosts(cert).ToHashSet();
            if (hosts.All(h => certHosts.Contains(h)))
            {
                // nothing to do, cert already has all the hosts it needs to have
                _log.LogInformation("Certificate already has all the needed hosts configured");
                return;
            }
        }

        try
        {
            if (await _kcert.AddChallengeHostsAsync(hosts))
            {
                _log.LogInformation("Giving challenge ingress time to propagate");
                await Task.Delay(TimeSpan.FromSeconds(10), tok);
            }

            await _kcert.RenewCertAsync(ns, name, hosts.ToArray());
            await _kcert.RemoveChallengeHostsAsync(hosts);
            await _email.NotifyRenewalResultAsync(ns, name, null);
        }
        catch (RenewalException ex)
        {
            await _email.NotifyRenewalResultAsync(ns, name, ex);
        }
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
            if (await _kcert.AddChallengeHostsAsync(hosts))
            {
                _log.LogInformation("Giving challenge ingress time to propagate");
                await Task.Delay(TimeSpan.FromSeconds(10), tok);
            }

            await _kcert.RenewCertAsync(secret.Namespace(), secret.Name());
            await _kcert.RemoveChallengeHostsAsync(hosts);
            await _email.NotifyRenewalResultAsync(secret.Namespace(), secret.Name(), null);
        }
        catch (RenewalException ex)
        {
            await _email.NotifyRenewalResultAsync(secret.Namespace(), secret.Name(), ex);
        }
    }
}
