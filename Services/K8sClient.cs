using k8s;
using k8s.Autorest;
using k8s.Models;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace KCert.Services;

[Service]
public class K8sClient(KCertConfig cfg, Kubernetes client, ILogger<K8sClient> log)
{
    private const string TlsSecretType = "kubernetes.io/tls";
    private const string CertLabelKey = "kcert.dev/secret";
    public const string IngressLabelKey = "kcert.dev/ingress";
    private const string TlsTypeSelector = "type=kubernetes.io/tls";

    private string IngressLabel => $"{IngressLabelKey}={cfg.IngressLabelValue}";
    private string ConfigMapLabel => $"{K8sWatchClient.CertRequestKey}={K8sWatchClient.CertRequestValue}";
    private string ManagedSecretLabel => $"{CertLabelKey}={cfg.IngressLabelValue}";
    private string UnManagedSecretLabel => $"!{CertLabelKey}";


    public IAsyncEnumerable<V1Ingress> GetAllIngressesAsync()
    {
        return IterateAsync<V1Ingress, V1IngressList>(GetAllIngressesAsync, GetNsIngressesAsync);
    }

    private Task<V1IngressList> GetAllIngressesAsync(string? tok) => client.ListIngressForAllNamespacesAsync(labelSelector: IngressLabel, continueParameter: tok);
    private Task<V1IngressList> GetNsIngressesAsync(string ns, string? tok) => client.ListNamespacedIngressAsync(ns, labelSelector: IngressLabel, continueParameter: tok);

    public async IAsyncEnumerable<V1ConfigMap> GetAllConfigMapsAsync()
    {
        log.LogInformation("GetAllConfigMapsAsync: Starting.");
        var receivedCount = 0;

        string currentSelector = string.IsNullOrEmpty(cfg.ConfigMapWatchLabelValue) ? cfg.ConfigMapWatchLabelKey : $"{cfg.ConfigMapWatchLabelKey}={cfg.ConfigMapWatchLabelValue}";
        log.LogInformation("GetAllConfigMapsAsync: Using API label selector: \"{selector}\"", currentSelector);

        IAsyncEnumerable<V1ConfigMap> configMaps = IterateAsync<V1ConfigMap, V1ConfigMapList>(GetAllConfigMapsInternalAsync, GetNsConfigMapsAsync);

        await foreach (var cm in configMaps)
        {
            receivedCount++;
            log.LogInformation("GetAllConfigMapsAsync: Processing ConfigMap #{Count} - {Namespace}/{Name} (matched API label selector)", receivedCount, cm.Namespace(), cm.Name());
            log.LogDebug("GetAllConfigMapsAsync: Labels for {Namespace}/{Name}: {Labels}", cm.Namespace(), cm.Name(), JsonSerializer.Serialize(cm.Metadata?.Labels ?? new Dictionary<string, string>()));
            log.LogInformation("GetAllConfigMapsAsync: Yielding ConfigMap {Namespace}/{Name}", cm.Namespace(), cm.Name());
            yield return cm;
        }

        log.LogInformation("GetAllConfigMapsAsync: Finished. Fetched and yielded {Count} ConfigMaps matching selector key='{Key}' value='{Value}'.", receivedCount, cfg.ConfigMapWatchLabelKey, cfg.ConfigMapWatchLabelValue);
    }

    private Task<V1ConfigMapList> GetAllConfigMapsInternalAsync(string? tok)
    {
        string newLabelSelector;
        if (string.IsNullOrEmpty(cfg.ConfigMapWatchLabelValue))
        {
            newLabelSelector = cfg.ConfigMapWatchLabelKey;
        }
        else
        {
            newLabelSelector = $"{cfg.ConfigMapWatchLabelKey}={cfg.ConfigMapWatchLabelValue}";
        }
        return client.ListConfigMapForAllNamespacesAsync(labelSelector: newLabelSelector, continueParameter: tok);
    }

    private Task<V1ConfigMapList> GetNsConfigMapsAsync(string ns, string? tok)
    {
        string newLabelSelector;
        if (string.IsNullOrEmpty(cfg.ConfigMapWatchLabelValue))
        {
            newLabelSelector = cfg.ConfigMapWatchLabelKey;
        }
        else
        {
            newLabelSelector = $"{cfg.ConfigMapWatchLabelKey}={cfg.ConfigMapWatchLabelValue}";
        }
        return client.ListNamespacedConfigMapAsync(ns, labelSelector: newLabelSelector, continueParameter: tok);
    }

    public IAsyncEnumerable<V1Secret> GetManagedSecretsAsync()
    {
        return IterateAsync<V1Secret, V1SecretList>(GetAllManagedSecretsAsync, GetNsManagedSecretsAsync);
    }

    private Task<V1SecretList> GetAllManagedSecretsAsync(string? tok) => client.ListSecretForAllNamespacesAsync(labelSelector: ManagedSecretLabel, continueParameter: tok);
    private Task<V1SecretList> GetNsManagedSecretsAsync(string ns, string? tok) => client.ListNamespacedSecretAsync(ns, labelSelector: ManagedSecretLabel, continueParameter: tok);

    public IAsyncEnumerable<V1Secret> GetUnManagedSecretsAsync()
    {
        return IterateAsync<V1Secret, V1SecretList>(GetAllUnManagedSecretsAsync, GetNsUnManagedSecretsAsync);
    }

    private Task<V1SecretList> GetAllUnManagedSecretsAsync(string? tok) => client.ListSecretForAllNamespacesAsync(fieldSelector: TlsTypeSelector, labelSelector: UnManagedSecretLabel, continueParameter: tok);
    private Task<V1SecretList> GetNsUnManagedSecretsAsync(string ns, string? tok) => client.ListNamespacedSecretAsync(ns, fieldSelector: TlsTypeSelector, labelSelector: UnManagedSecretLabel, continueParameter: tok);

    public async Task<V1Secret?> GetSecretAsync(string ns, string name)
    {
        try
        {
            return await client.ReadNamespacedSecretAsync(name, ns);
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

    public async Task<V1Ingress?> GetIngressAsync(string ns, string name)
    {
        try
        {
            return await client.ReadNamespacedIngressAsync(name, ns);
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
            await client.DeleteNamespacedIngressAsync(name, ns);
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
        await client.CreateNamespacedIngressAsync(ingress, cfg.KCertNamespace);
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
                await client.ReplaceNamespacedSecretAsync(secret, name, ns);
                return;
            }

            // if it's an opaque secret (ie: a request to create a cert) we delete it and create the cert
            await client.DeleteNamespacedSecretAsync(name, ns);
        }

        secret = InitSecret(name);
        UpdateSecretData(secret, ns, name, key, cert);
        await client.CreateNamespacedSecretAsync(secret, ns);
    }

    private void UpdateSecretData(V1Secret secret, string ns, string name, string key, string cert)
    {
        if (secret.Type != TlsSecretType)
        {
            throw new Exception($"Secret {ns}:{name} is not a TLS secret type");
        }

        secret.Metadata.Labels ??= new Dictionary<string, string>();
        secret.Metadata.Labels[CertLabelKey] = cfg.IngressLabelValue;
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

    private delegate Task<TList> ListAllFunc<TList>(string? tok);
    private delegate Task<TList> ListNsFunc<TList>(string ns, string? tok);

    private IAsyncEnumerable<TItem> IterateAsync<TItem, TList>(ListAllFunc<TList> all, ListNsFunc<TList> byNs) where TList : IKubernetesObject<V1ListMeta>, IItems<TItem>
    {
        return cfg.NamespaceConstraints.Length == 0
            ? IterateAsync<TItem, TList>(all)
            : IterateAsync<TItem, TList>(byNs);
    }

    private static async IAsyncEnumerable<TItem> IterateAsync<TItem, TList>(ListAllFunc<TList> callback) where TList : IKubernetesObject<V1ListMeta>, IItems<TItem>
    {
        string? tok = null;
        do
        {
            var result = await callback(tok);
            tok = result.Continue();
            foreach (var item in result.Items)
            {
                yield return item;
            }
        } while (tok != null);
    }

    private async IAsyncEnumerable<TItem> IterateAsync<TItem, TList>(ListNsFunc<TList> callback) where TList : IKubernetesObject<V1ListMeta>, IItems<TItem>
    {
        foreach (var ns in cfg.NamespaceConstraints)
        {
            await foreach (var item in IterateAsync<TItem, TList>(tok => callback(ns, tok)))
            {
                yield return item;
            }
        }
    }
}
