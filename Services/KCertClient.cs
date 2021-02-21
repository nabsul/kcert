using k8s.Models;
using KCert.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
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


        public async Task<IList<V1Ingress>> GetAllIngressesAsync()
        {
            return await _kube.GetAllIngressesAsync();
        }

        public async Task<V1Ingress> GetIngressAsync(string ns, string name)
        {
            return await _kube.GetIngressAsync(ns, name);
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

        public async Task<string> GetThumbprintAsync()
        {
            var p = await GetConfigAsync();
            return _cert.GetThumbprint(p.AcmeKey);
        }

        public async Task GetCertAsync(string ns, string secretName)
        {
            var p = await GetConfigAsync();
            var ingresses = await GetAllIngressesAsync();
            var tlsEntries = ingresses.Where(i => i.Namespace() == ns).SelectMany(i => i.Spec.Tls);
            var hosts = tlsEntries.Where(t => t.SecretName == secretName).SelectMany(t => t.Hosts).ToArray();
            await _getCert.GetCertAsync(ns, secretName, hosts, p);
        }

        public async Task SyncHostsAsync()
        {
            var ingresses = await GetAllIngressesAsync();
            var allHosts = ingresses.SelectMany(i => i.Spec.Tls.SelectMany(r => r.Hosts)).Distinct().ToList();
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
                return await GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
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
