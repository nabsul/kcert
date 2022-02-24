using k8s.Models;
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

    public KCertClient(K8sClient kube, KCertConfig cfg, RenewalHandler getCert, CertClient cert)
    {
        _kube = kube;
        _cfg = cfg;
        _getCert = getCert;
        _cert = cert;
    }

    public async Task RenewCertAsync(string ns, string secretName, string[] hosts = null)
    {
        if (hosts == null)
        {
            var secret = await _kube.GetSecretAsync(ns, secretName);
            if (secret == null)
            {
                throw new Exception($"Secret not found: {ns} - {secretName}");
            }

            var cert = _cert.GetCert(secret);
            hosts = _cert.GetHosts(cert).ToArray();
        }

        await _getCert.RenewCertAsync(ns, secretName, hosts);
    }

    public async Task<bool> AddChallengeHostsAsync(IEnumerable<string> hosts)
    {
        var kcertIngress = await _kube.GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
        var configuredHosts = kcertIngress.Spec.Rules.Select(r => r.Host).ToHashSet();
        var changed = false;
        foreach (var host in hosts.Where(h => !configuredHosts.Contains(h)))
        {
            changed = true;
            kcertIngress.Spec.Rules.Add(CreateRule(host));
        }

        if (changed)
        {
            await _kube.UpdateIngressAsync(kcertIngress);
        }

        return changed;
    }

    public async Task<bool> RemoveChallengeHostsAsync(IEnumerable<string> hosts)
    {
        var toRemove = hosts.ToHashSet();
        var kcertIngress = await _kube.GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);

        var filteredRules = kcertIngress.Spec.Rules.Where(r => !toRemove.Contains(r.Host)).ToList();

        if (filteredRules.Count == kcertIngress.Spec.Rules.Count)
        {
            return false;
        }

        kcertIngress.Spec.Rules = filteredRules;
        await _kube.UpdateIngressAsync(kcertIngress);
        return true;
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
