using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KCert.Services
{
    public class KCertClient
    {
        private readonly K8sClient _kube;
        private readonly RenewalHandler _getCert;
        private readonly CertClient _cert;
        private readonly KCertConfig _cfg;
        private readonly ILogger<KCertClient> _log;

        public KCertClient(K8sClient kube, KCertConfig cfg, RenewalHandler getCert, CertClient cert, ILogger<KCertClient> log)
        {
            _kube = kube;
            _cfg = cfg;
            _getCert = getCert;
            _cert = cert;
            _log = log;
        }

        public async Task<KCertParams> GetConfigAsync()
        {
            var s = await _kube.GetSecretAsync(_cfg.KCertNamespace, _cfg.KCertSecretName);
            return s == null ? null : new KCertParams(s);
        }

        public async Task SaveConfigAsync(KCertParams p)
        {
            await _kube.SaveSecretDataAsync(_cfg.KCertNamespace, _cfg.KCertSecretName, p.Export());
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

            var p = await GetConfigAsync();
            await _getCert.RenewCertAsync(ns, secretName, hosts, p);
        }

        public async Task SyncHostsAsync()
        {
            var secrets = await _kube.GetManagedSecretsAsync();
            var allHosts = secrets
                .Select(_cert.GetCert)
                .SelectMany(_cert.GetHosts)
                .Distinct().ToList();

            if (allHosts.Count == 0)
            {
                _log.LogWarning("SyncHostsAsync: Nothing to do because there are no ingresses/hosts");
                return;
            }

            var kcertIngress = await GetKCertIngressAsync() ?? CreateKCertIngress();
            kcertIngress.Spec.Rules = allHosts.Select(CreateRule).ToList();
            await _kube.UpdateIngressAsync(kcertIngress);
        }

        public async Task<V1Ingress> GetKCertIngressAsync()
        {
            try
            {
                return await _kube.GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
            }
            catch (HttpOperationException ex)
            {
                if (ex.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw;
            }
        }

        private V1Ingress CreateKCertIngress()
        {
            return new V1Ingress
            {
                Metadata = new V1ObjectMeta
                {
                    Name = _cfg.KCertIngressName,
                    NamespaceProperty = _cfg.KCertNamespace,
                    Annotations = new Dictionary<string, string> { { "kubernetes.io/ingress.class", "nginx" } },
                },
                Spec = new V1IngressSpec(),
            };
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
}
