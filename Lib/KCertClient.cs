using k8s.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
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

        public KCertClient(K8sClient kube, KCertConfig cfg, GetCertHandler getCert, NamespaceFilter filter)
        {
            _kube = kube;
            _cfg = cfg;
            _getCert = getCert;
            _filter = filter;
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

        private static ECDsa GetSigner(string key)
        {
            var sign = ECDsa.Create();
            sign.KeySize = 256;
            sign.ImportECPrivateKey(Base64UrlTextEncoder.Decode(key), out _);
            return sign;
        }
    }
}
