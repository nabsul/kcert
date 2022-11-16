using k8s.Autorest;
using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class KCertClient
{
    private readonly K8sClient _kube;
    private readonly RenewalHandler _getCert;
    private readonly KCertConfig _cfg;
    private readonly ILogger<KCertClient> _log;
    private readonly EmailClient _email;
    private readonly CertClient _cert;
    private readonly CallbackIngressService _callbackIngressService;

    private Task _running = Task.CompletedTask;
    
    public KCertClient(
        K8sClient kube,
        KCertConfig cfg,
        RenewalHandler getCert,
        ILogger<KCertClient> log,
        EmailClient email,
        CertClient cert,
        CallbackIngressService callbackIngressService)
    {
        _kube = kube;
        _cfg = cfg;
        _getCert = getCert;
        _log = log;
        _email = email;
        _cert = cert;
        _callbackIngressService = callbackIngressService;
    }

    public async Task RenewIfNeededAsync(string ns, string name, string[] hosts, CancellationToken tok)
    {
        var secret = await _kube.GetSecretAsync(ns, name);
        tok.ThrowIfCancellationRequested();

        if (secret != null)
        {
            var cert = _cert.GetCert(secret);
            var certHosts = _cert.GetHosts(cert).ToHashSet();
            if (hosts.Length == certHosts.Count && hosts.All(h => certHosts.Contains(h)))
            {
                // nothing to do, cert already has all the hosts it needs to have
                _log.LogInformation("Certificate already has all the needed hosts configured");
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
            _log.LogError(ex, "Previous task in rewal chain failed.");
        }

        try
        {
            await AddChallengeHostsAsync(hosts);
            tok.ThrowIfCancellationRequested();
            await _getCert.RenewCertAsync(ns, secretName, hosts);
            await _kube.DeleteIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
            await _email.NotifyRenewalResultAsync(ns, secretName, null);
            tok.ThrowIfCancellationRequested();
        }
        catch (RenewalException ex)
        {
            _log.LogError(ex, "Renewal failed");
            await _email.NotifyRenewalResultAsync(ns, secretName, ex);
        }
        catch (HttpOperationException ex)
        {
            _log.LogError(ex, "HTTP Operation failed with respones: {resp}", ex.Response.Content);
            throw;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Unexpected renewal failure");
        }
    }

    private async Task AddChallengeHostsAsync(IEnumerable<string> hosts)
    {
        var kcertIngress = await _kube.GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
        if (kcertIngress != null)
        {
            await _kube.DeleteIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
        }

        var hostList = hosts.ToList();
        kcertIngress = new()
        {
            Metadata = new()
            {
                Name = _cfg.KCertIngressName,
                NamespaceProperty = _cfg.KCertNamespace,
            },
            Spec = new()
            {
                Rules = hostList.Select(CreateRule).ToList()
            }
        };

        if (_cfg.UseChallengeIngressClassName)
        {
            kcertIngress.Spec.IngressClassName = _cfg.ChallengeIngressClassName;
        }

        if (_cfg.UseChallengeIngressAnnotations)
        {
            kcertIngress.Metadata.Annotations = _cfg.ChallengeIngressAnnotations;
        }

        if (_cfg.UseChallengeIngressLabels)
        {
            kcertIngress.Metadata.Labels = _cfg.ChallengeIngressLabels;
        }

        await _kube.CreateIngressAsync(kcertIngress);
        _log.LogInformation("Giving challenge ingress time to propagate");

        await _callbackIngressService.AwaitChallengeIngressPropagatedAsync(hostList);
    }

    private V1IngressRule CreateRule(string host)
    {

        var path = new V1HTTPIngressPath
        {
            Path = "/.well-known/acme-challenge/",
            PathType = "Prefix",
            Backend = new()
            {
                Service = new()
                {
                    Name = _cfg.KCertServiceName,
                    Port = new(number: _cfg.KCertServicePort)
                },
            },
        };

        return new()
        {
            Host = host,
            Http = new()
            {
                Paths = new List<V1HTTPIngressPath>() { path }
            },
        };
    }
}
