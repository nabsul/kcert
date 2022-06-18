using k8s.Autorest;
using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

    private Task _running = Task.CompletedTask;

    public KCertClient(K8sClient kube, KCertConfig cfg, RenewalHandler getCert, ILogger<KCertClient> log, EmailClient email)
    {
        _kube = kube;
        _cfg = cfg;
        _getCert = getCert;
        _log = log;
        _email = email;
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

        kcertIngress = new()
        {
            Metadata = new()
            {
                Name = _cfg.KCertIngressName,
                NamespaceProperty = _cfg.KCertNamespace,
                Annotations = _cfg.ChallengeIngressAnnotations,
            },
            Spec = new()
            {
                Rules = hosts.Select(CreateRule).ToList()
            }
        };

        await _kube.CreateIngressAsync(kcertIngress);
        _log.LogInformation("Giving challenge ingress time to propagate");
        await Task.Delay(TimeSpan.FromSeconds(5));
    }

    private V1IngressRule CreateRule(string host)
    {
        return new V1IngressRule
        {
            Host = host,
            Http = new V1HTTPIngressRuleValue
            {
                Paths = new List<V1HTTPIngressPath>()
                    {
                        new V1HTTPIngressPath
                        {
                            Path = "/.well-known/acme-challenge/",
                            PathType = "Prefix",
                            Backend = new V1IngressBackend
                            {
                                Service = new V1IngressServiceBackend
                                {
                                    Name = _cfg.KCertServiceName,
                                    Port = new V1ServiceBackendPort(number: _cfg.KCertServicePort)
                                },
                            },
                        },
                    },
            },
        };
    }
}
