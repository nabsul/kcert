using k8s;
using k8s.Exceptions;
using k8s.Models;
using Microsoft.Rest;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class K8sClient
{
    private const string TlsSecretType = "kubernetes.io/tls";
    private const string LabelKey = "kcert.dev/label";
    private const string TlsTypeSelector = "type=kubernetes.io/tls";

    private readonly KCertConfig _cfg;
    private readonly Kubernetes _client;

    public K8sClient(KCertConfig cfg)
    {
        _cfg = cfg;
        _client = new Kubernetes(GetConfig());
    }

    public async Task WatchIngressesAsync(Action<WatchEventType, V1Ingress> callback, CancellationToken tok)
    {
        var message = _client.ListIngressForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok);
        await foreach (var (type, item) in message.WatchAsync<V1Ingress, V1IngressList>())
        {
            callback(type, item);
        }
    }

    public async Task<List<V1Secret>> GetManagedSecretsAsync()
    {
        var result = await _client.ListSecretForAllNamespacesAsync(fieldSelector: TlsTypeSelector, labelSelector: $"{LabelKey}={_cfg.Label}");
        return result.Items.ToList();
    }

    public async Task<List<V1Secret>> GetUnmanagedSecretsAsync()
    {
        var result = await _client.ListSecretForAllNamespacesAsync(fieldSelector: TlsTypeSelector, labelSelector: $"!{LabelKey}");
        return result.Items.ToList();
    }

    public async Task ManageSecretAsync(string ns, string name)
    {
        var secret = await _client.ReadNamespacedSecretAsync(name, ns);
        secret.Metadata.Labels ??= new Dictionary<string, string>();
        secret.Metadata.Labels[LabelKey] = _cfg.Label;
        await _client.ReplaceNamespacedSecretAsync(secret, name, ns);
    }

    public async Task UnmanageSecretAsync(string ns, string name)
    {
        var secret = await _client.ReadNamespacedSecretAsync(name, ns);
        secret.Metadata.Labels = secret.Metadata.Labels ?? new Dictionary<string, string>();
        secret.Metadata.Labels.Remove(LabelKey);
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

    public async Task UpsertIngressAsync(string ns, string name, Action<V1Ingress> setValues)
    {
        var ingress = await GetIngressAsync(ns, name);
        if (ingress == null)
        {
            await CreateIngressAsync(ns, name, setValues);
            return;
        }

        setValues(ingress);
        await _client.ReplaceNamespacedIngressAsync(ingress, name, ns);
    }

    private async Task CreateIngressAsync(string ns, string name, Action<V1Ingress> setValues)
    {
        var ingress = new V1Ingress
        {
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = ns,
                Annotations = new Dictionary<string, string> { { "kubernetes.io/ingress.class", "nginx" } },
            },
            Spec = new V1IngressSpec(),
        };

        setValues(ingress);
        await _client.CreateNamespacedIngressAsync(ingress, ns);
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

        secret.Metadata.Labels ??= new Dictionary<string, string>();
        secret.Metadata.Labels[LabelKey] = _cfg.Label;
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
