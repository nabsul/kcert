using k8s;
using k8s.Models;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace KCert.Lib
{
    [Service]
    public class K8sClient
    {
        private const string TlsSecretType = "kubernetes.io/tls";

        private readonly KCertConfig _cfg;
        private readonly Kubernetes _client;

        public K8sClient(KCertConfig cfg)
        {
            _cfg = cfg;

            var file = cfg.K8sConfigFile;
            var k8sCfg = string.IsNullOrWhiteSpace(file)
                ? KubernetesClientConfiguration.InClusterConfig()
                : KubernetesClientConfiguration.BuildConfigFromConfigFile(file);
            _client = new Kubernetes(k8sCfg);
        }

        public async Task<V1Service> GetServiceAsync(string ns)
        {
            try
            {
                return await _client.ReadNamespacedServiceAsync(_cfg.KCertServiceName, ns);
            }
            catch (HttpOperationException ex)
            {
                if (ex.Response.StatusCode != HttpStatusCode.NotFound)
                {
                    throw;
                }

                return null;
            }
        }

        public async Task CreateServiceAsync(string ns)
        {
            var (name, kcertNs, servicePort) = (_cfg.KCertServiceName, _cfg.KCertNamespace, _cfg.KCertServicePort);
            var svc = await GetServiceAsync(ns);

            if (svc != null)
            {
                svc.Spec = GetServiceSpec(kcertNs, name, servicePort);
                await _client.ReplaceNamespacedServiceAsync(svc, name, ns);
                return;
            }

            svc = new V1Service
            {
                Metadata = new V1ObjectMeta { Name = name },
                Spec = GetServiceSpec(kcertNs, name, servicePort),
            };
            await _client.CreateNamespacedServiceAsync(svc, ns);
        }

        private static V1ServiceSpec GetServiceSpec(string ns, string name, string servicePort)
        {
            return new V1ServiceSpec
            {
                Type = "ExternalName",
                ExternalName = $"{name}.{ns}",
                Ports = new List<V1ServicePort>
                {
                    new V1ServicePort
                    {
                        Name = servicePort,
                        Port = 80,
                        TargetPort = 80,
                    }
                },
            };
        }

        public async Task DeleteServiceAsync(string ns)
        {
            await _client.DeleteNamespacedServiceAsync(_cfg.KCertServiceName, ns);
        }

        public async Task<IList<V1Secret>> GetAllSecretsAsync(string ns)
        {
            var result = await _client.ListNamespacedSecretAsync(ns);
            return result.Items;
        }

        public async Task<V1Secret> GetSecretAsync(string ns, string name)
        {
            try
            {
                return await _client.ReadNamespacedSecretAsync(name, ns);
            }
            catch (HttpOperationException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task SaveSecretDataAsync(string ns, string name, IDictionary<string, byte[]> data)
        {
            var secret = await GetSecretAsync(ns, name);
            if (secret == null)
            {
                await CreateSecretAsync(ns, name, data);
                return;
            }

            await UpdateSecretAsync(ns, name, secret, data);
        }

        private async Task UpdateSecretAsync(string ns, string name, V1Secret secret, IDictionary<string, byte[]> data)
        {
            secret.Data = data;
            await _client.ReplaceNamespacedSecretAsync(secret, name, ns);
        }

        private async Task CreateSecretAsync(string ns, string name, IDictionary<string, byte[]> data)
        {
            var secret = new V1Secret
            {
                Metadata = new V1ObjectMeta
                {
                    Name = name,
                },
                Type = "Opaque",
                Data = data,
            };

            await _client.CreateNamespacedSecretAsync(secret, ns);
        }

        public async Task<IList<Networkingv1beta1Ingress>> GetAllIngressesAsync()
        {
            var result = await _client.ListIngressForAllNamespaces2Async();
            return result.Items;
        }

        public async Task<Networkingv1beta1Ingress> GetIngressAsync(string ns, string name)
        {
            try
            {
                return await _client.ReadNamespacedIngress2Async(name, ns);
            }
            catch(HttpOperationException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                throw;
            }
        }

        public async Task UpdateIngressAsync(Networkingv1beta1Ingress ingress)
        {
            var old = await GetIngressAsync(ingress.Namespace(), ingress.Name());
            if (old == null)
            {
                await _client.CreateNamespacedIngress2Async(ingress, ingress.Namespace());
                return;
            }

            await _client.ReplaceNamespacedIngress2Async(ingress, ingress.Name(), ingress.Namespace());
        }

        public async Task UpdateTlsSecretAsync(string ns, string name, string key, string cert)
        {
            bool create = false;
            var secret = await GetSecretAsync(ns, name);
            if (secret == null)
            {
                secret = InitSecret(name);
                create = true;
            }

            if (secret.Type != TlsSecretType)
            {
                throw new Exception($"Secret {ns}:{name} is not a TLS secret type");
            }

            secret.Data["tls.key"] = Encoding.UTF8.GetBytes(key);
            secret.Data["tls.crt"] = Encoding.UTF8.GetBytes(cert);
            var task = create ? _client.CreateNamespacedSecretAsync(secret, ns) : _client.ReplaceNamespacedSecretAsync(secret, name, ns);
            await task;
        }

        private static V1Secret InitSecret(string name)
        {
            return new V1Secret
            {
                ApiVersion = "v1",
                Kind = "Secret",
                Type = TlsSecretType,
                Data = new Dictionary<string, byte[]>(),
                Metadata = new V1ObjectMeta
                {
                    Name = name
                }
            };
        }
    }
}
