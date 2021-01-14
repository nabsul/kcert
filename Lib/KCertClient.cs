using k8s.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KCert.Lib
{
    public class KCertClient
    {
        private readonly K8sClient _kube;
        private readonly GetCertHandler _getCert;
        private readonly KCertConfig _cfg;
        private readonly NamespaceFilter _filter;
        private readonly Logger<KCertClient> _log;

        public KCertClient(K8sClient kube, KCertConfig cfg, GetCertHandler getCert, NamespaceFilter filter, Logger<KCertClient> log)
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
            return ingresses.Where(i => _filter.IsManagedNamespace(i.Namespace())).ToList();
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

        public async Task<GetCertResult> GetCertAsync(string ns, string ingressName)
        {
            var p = await GetConfigAsync();
            return await _getCert.GetCertAsync(ns, ingressName, p, GetSigner(p.AcmeKey));
        }

        public async Task SyncHostsAsync()
        {
            var ingresses = await GetAllIngressesAsync();
            var allHosts = ingresses.SelectMany(i => i.Hosts());
            if (allHosts.Count() == 0)
            {
                _log.LogWarning("Nothing to do because there are no ingresses/hosts");
            }

            var kcertIngress = await GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
            if (kcertIngress == null)
            {
                kcertIngress = CreateKCertIngress();
            }

            kcertIngress.Spec.Rules = allHosts.Select(CreateRule).ToList();
            await _kube.UpdateIngressAsync(kcertIngress);
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
