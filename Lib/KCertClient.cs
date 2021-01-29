using k8s.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KCert.Lib
{
    [Service]
    public class KCertClient
    {
        private readonly K8sClient _kube;
        private readonly RenewalHandler _getCert;
        private readonly KCertConfig _cfg;
        private readonly NamespaceFilter _filter;
        private readonly ILogger<KCertClient> _log;

        public KCertClient(K8sClient kube, KCertConfig cfg, RenewalHandler getCert, NamespaceFilter filter, ILogger<KCertClient> log)
        {
            _kube = kube;
            _cfg = cfg;
            _getCert = getCert;
            _filter = filter;
            _log = log;
        }

        public string GenerateNewKey()
        {
            var sign = ECDsa.Create();
            sign.KeySize = 256;
            return Base64UrlTextEncoder.Encode(sign.ExportECPrivateKey());
        }

        public async Task<IList<Networkingv1beta1Ingress>> GetAllIngressesAsync()
        {
            var ingresses = await _kube.GetAllIngressesAsync();
            return ingresses.Where(_filter.IsManagedIngress).ToList();
        }

        public async Task<Networkingv1beta1Ingress> GetIngressAsync(string ns, string name)
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
            var sign = GetSigner(p.AcmeKey);
            var jwk = sign.GetJwk();
            var jwkJson = JsonSerializer.Serialize(jwk);
            var jwkBytes = Encoding.UTF8.GetBytes(jwkJson);
            using var hasher = SHA256.Create();
            var result = hasher.ComputeHash(jwkBytes);
            return Base64UrlTextEncoder.Encode(result);
        }

        public async Task<RenewalResult> GetCertAsync(string ns, string secretName)
        {
            var p = await GetConfigAsync();
            var ingresses = await GetAllIngressesAsync();
            var tlsEntries = ingresses.Where(i => i.Namespace() == ns).SelectMany(i => i.Spec.Tls);
            var hosts = tlsEntries.Where(t => t.SecretName == secretName).SelectMany(t => t.Hosts).ToArray();
            return await _getCert.GetCertAsync(ns, secretName, hosts, p, GetSigner(p.AcmeKey));
        }

        public async Task SyncHostsAsync()
        {
            var ingresses = await GetAllIngressesAsync();
            var allHosts = ingresses.SelectMany(i => i.Hosts()).Distinct().ToList();
            if (allHosts.Count == 0)
            {
                _log.LogWarning("SyncHostsAsync: Nothing to do because there are no ingresses/hosts");
                return;
            }

            var kcertIngress = await GetKCertIngressAsync() ?? CreateKCertIngress();
            kcertIngress.Spec.Rules = allHosts.Select(CreateRule).ToList();
            await _kube.UpdateIngressAsync(kcertIngress);
        }

        public async Task<Networkingv1beta1Ingress> GetKCertIngressAsync()
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

        private Networkingv1beta1Ingress CreateKCertIngress()
        {
            return new Networkingv1beta1Ingress
            {
                Metadata = new V1ObjectMeta
                {
                    Name = _cfg.KCertIngressName,
                    NamespaceProperty = _cfg.KCertNamespace,
                    Annotations = new Dictionary<string, string> { { "kubernetes.io/ingress.class", "nginx" } },
                },
                Spec = new Networkingv1beta1IngressSpec(),
            };
        }

        private Networkingv1beta1IngressRule CreateRule(string host)
        {
            return new Networkingv1beta1IngressRule
            {
                Host = host,
                Http = new Networkingv1beta1HTTPIngressRuleValue
                {
                    Paths = new List<Networkingv1beta1HTTPIngressPath>()
                    {
                        new Networkingv1beta1HTTPIngressPath
                        {
                            Path = "/.well-known/acme-challenge/",
                            PathType = "Prefix",
                            Backend = new Networkingv1beta1IngressBackend
                            {
                                ServiceName = _cfg.KCertServiceName,
                                ServicePort = _cfg.KCertServicePort,
                            },
                        },
                    },
                },
            };
        }

        private static ECDsa GetSigner(string key)
        {
            var sign = ECDsa.Create();
            sign.KeySize = 256;
            sign.ImportECPrivateKey(Base64UrlTextEncoder.Decode(key), out _);
            return sign;
        }
    }
}
