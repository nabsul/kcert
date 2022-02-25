using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class KCertClient
{
    private readonly K8sClient _kube;
    private readonly RenewalHandler _getCert;
    private readonly CertClient _cert;
    private readonly KCertConfig _cfg;
    private readonly ILogger<KCertClient> _log;
    private readonly EmailClient _email;

    private Task _running = Task.CompletedTask;

    public KCertClient(K8sClient kube, KCertConfig cfg, RenewalHandler getCert, CertClient cert, ILogger<KCertClient> log, EmailClient email)
    {
        _kube = kube;
        _cfg = cfg;
        _getCert = getCert;
        _cert = cert;
        _log = log;
        _email = email;
    }

    // Ensure that only one cert is renewed at a time
    public Task StartRenewalProcessAsync(string ns, string secretName, string[] hosts)
    {
        Task task;
        lock(this)
        {
            task = RenewCertAsync(_running, ns, secretName, hosts);
            _running = task;
        }

        return task;
    }

    private async Task RenewCertAsync(Task prev, string ns, string secretName, string[] hosts)
    {
        try
        {
            await prev;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Previous task in rewal chain failed.");
        }

        try
        {
            await AddChallengeHostsAsync(hosts);
            _log.LogInformation("Giving challenge ingress time to propagate");
            await _getCert.RenewCertAsync(ns, secretName, hosts);
            await _kube.DeleteIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
            await _email.NotifyRenewalResultAsync(ns, secretName, null);
        }
        catch (RenewalException ex)
        {
            _log.LogError(ex, "Renewal failed");
            await _email.NotifyRenewalResultAsync(ns, secretName, ex);
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
            },
            Spec = new()
            {
                Rules = hosts.Select(CreateRule).ToList()
            }
        };

        var annotation = _cfg.ChallengeIngressAnnotation;
        if (annotation != null)
        {
            var parts = annotation.Split(":", 2);
            kcertIngress.Metadata.Annotations = new Dictionary<string, string>();
            kcertIngress.Metadata.Annotations.Add(parts[0], parts[1]);
        }

        await _kube.CreateIngressAsync(kcertIngress);
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
