using k8s;
using k8s.Autorest;
using k8s.Exceptions;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class K8sClient
{
    private const string TlsSecretType = "kubernetes.io/tls";
    private const string CertLabelKey = "kcert.dev/secret";
    public const string IngressLabelKey = "kcert.dev/ingress";
    private const string TlsTypeSelector = "type=kubernetes.io/tls";
    private readonly KCertConfig _cfg;

    private readonly ILogger<K8sClient> _log;

    private readonly Kubernetes _client;

    private readonly bool _namespaceConstraigned;

    public K8sClient(KCertConfig cfg, ILogger<K8sClient> log)
    {
        _cfg = cfg;
        _log = log;
        _client = new Kubernetes(GetConfig());

        _namespaceConstraigned = _cfg.NamespaceConstraintsList.Count > 0;
    }

    public async IAsyncEnumerable<V1Ingress> GetAllIngressesAsync()
    {
        var label = $"{IngressLabelKey}={_cfg.IngressLabelValue}";

        var requests = new List<Func<string, Task<V1IngressList>>>();
        if (!_namespaceConstraigned)
        {
            requests.Add((tok) => _client.ListIngressForAllNamespacesAsync(labelSelector: label, continueParameter: tok));
        }
        else
        {
            foreach (var n in _cfg.NamespaceConstraintsList)
            {
                requests.Add((tok) => _client.ListNamespacedIngressAsync(n, labelSelector: label, continueParameter: tok));
            }
        }

        foreach (var callback in requests)
        {
            await foreach(var ing in Helpers.K8sEnumerateAsync<V1Ingress, V1IngressList>(callback))
            {
                yield return ing;
            }
        }
    }

    public async IAsyncEnumerable<V1ConfigMap> GetAllConfigMapsAsync()
    {
        var label = $"{K8sWatchClient.CertRequestKey}={K8sWatchClient.CertRequestValue}";

        var requests = new List<Func<string, Task<V1ConfigMapList>>>();
        if(!_namespaceConstraigned)
        {
            requests.Add((tok) => _client.ListConfigMapForAllNamespacesAsync(labelSelector: label, continueParameter: tok));
        }
        else {
            foreach (var n in _cfg.NamespaceConstraintsList)
            {
                requests.Add((tok) => _client.ListNamespacedConfigMapAsync(n, labelSelector: label, continueParameter: tok));
            }
        }

        foreach(var callback in requests)
        {
            await foreach(var ing in Helpers.K8sEnumerateAsync<V1ConfigMap, V1ConfigMapList>(callback))
            {
                yield return ing;
            }
        }
    }

    public async Task<List<V1Secret>> GetManagedSecretsAsync()
    {
        if(_namespaceConstraigned)
        {
            return await GetNamespacedManagedSecretsAsync(_cfg.NamespaceConstraintsList);
        }
        else
        {
            return await GetAllNamespacesManagedSecretsAsync();
        }
    }

    public async Task<List<V1Secret>> GetUnManagedSecretsAsync()
    {
        if(_namespaceConstraigned)
        {
            return await GetNamespacedUnmanagedSecretsAsync(_cfg.NamespaceConstraintsList);
        }
        else
        {
            return await GetAllNamespacesUnmanagedSecretsAsync();
        }
    }

    public async Task<List<V1Secret>> GetAllNamespacesManagedSecretsAsync()
    {
        var result = await _client.ListSecretForAllNamespacesAsync(fieldSelector: TlsTypeSelector, labelSelector: $"{CertLabelKey}={_cfg.IngressLabelValue}");
        return result.Items.ToList();
    }

    public async Task<List<V1Secret>> GetAllNamespacesUnmanagedSecretsAsync()
    {
        var result = await _client.ListSecretForAllNamespacesAsync(fieldSelector: TlsTypeSelector, labelSelector: $"!{CertLabelKey}");
        return result.Items.ToList();
    }

    public async Task<List<V1Secret>> GetNamespacedManagedSecretsAsync(List<string> namespaces)
    {
        var label = $"{CertLabelKey}={_cfg.IngressLabelValue}";
        var results = await GetNamespacedSecretsAsync(namespaces, label, "");

        return results.Items.ToList();
    }

    public async Task<List<V1Secret>> GetNamespacedUnmanagedSecretsAsync(List<string> namespaces)
    {
        var label = $"!{CertLabelKey}";
        var results = await GetNamespacedSecretsAsync(namespaces, label, "");

        return results.Items.ToList();
    }

    public async Task<V1SecretList> GetNamespacedSecretsAsync(List<string> namespaces, string label, string tok)
    {
        return await namespaces
                    .Select(async ns => await _client.ListNamespacedSecretAsync(ns, labelSelector: label, continueParameter: tok))
                    .Aggregate(async (current, next) => 
                        {
                            return new V1SecretList((await current).Items.Concat((await next).Items).ToList());
                        });
    }

    public async Task ManageSecretAsync(string ns, string name)
    {
        var secret = await _client.ReadNamespacedSecretAsync(name, ns);
        secret.Metadata.Labels ??= new Dictionary<string, string>();
        secret.Metadata.Labels[CertLabelKey] = _cfg.IngressLabelValue;
        await _client.ReplaceNamespacedSecretAsync(secret, name, ns);
    }

    public async Task UnmanageSecretAsync(string ns, string name)
    {
        var secret = await _client.ReadNamespacedSecretAsync(name, ns);
        secret.Metadata.Labels = secret.Metadata.Labels ?? new Dictionary<string, string>();
        secret.Metadata.Labels.Remove(CertLabelKey);
        await _client.ReplaceNamespacedSecretAsync(secret, name, ns);
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

    public async Task DeleteSecretAsync(string ns, string name)
    {
        await _client.DeleteNamespacedSecretAsync(name, ns);
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

    public async Task<V1Ingress> GetIngressAsync(string ns, string name)
    {
        try
        {
            return await _client.ReadNamespacedIngressAsync(name, ns);
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

    public async Task DeleteIngressAsync(string ns, string name)
    {
        try
        {
            await _client.DeleteNamespacedIngressAsync(name, ns);
        }
        catch (HttpOperationException ex)
        {
            if (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            throw;
        }
    }

    public async Task CreateIngressAsync(V1Ingress ingress)
    {
        await _client.CreateNamespacedIngressAsync(ingress, _cfg.KCertNamespace);
    }

    public async Task UpdateTlsSecretAsync(string ns, string name, string key, string cert)
    {
        var secret = await GetSecretAsync(ns, name);
        if (secret != null)
        {
            // if it's a cert we can directly replace it
            if (secret.Type == TlsSecretType)
            {
                UpdateSecretData(secret, ns, name, key, cert);
                await _client.ReplaceNamespacedSecretAsync(secret, name, ns);
                return;
            }

            // if it's an opaque secret (ie: a request to create a cert) we delete it and create the cert
            await _client.DeleteNamespacedSecretAsync(name, ns);
        }

        secret = InitSecret(name);
        UpdateSecretData(secret, ns, name, key, cert);
        await _client.CreateNamespacedSecretAsync(secret, ns);
    }

    private void UpdateSecretData(V1Secret secret, string ns, string name, string key, string cert)
    {
        if (secret.Type != TlsSecretType)
        {
            throw new Exception($"Secret {ns}:{name} is not a TLS secret type");
        }

        secret.Metadata.Labels ??= new Dictionary<string, string>();
        secret.Metadata.Labels[CertLabelKey] = _cfg.IngressLabelValue;
        secret.Data["tls.key"] = Encoding.UTF8.GetBytes(key);
        secret.Data["tls.crt"] = Encoding.UTF8.GetBytes(cert);
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

    private KubernetesClientConfiguration GetConfig()
    {
        try
        {
            return KubernetesClientConfiguration.InClusterConfig();
        }
        catch (KubeConfigException)
        {
            return KubernetesClientConfiguration.BuildConfigFromConfigFile(_cfg.K8sConfigFile);
        }
    }
}