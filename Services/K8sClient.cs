using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.AspNetCore.Components.Forms;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace KCert.Services;

public class K8sClient(KCertConfig cfg, Kubernetes client)
{
    private const string TlsSecretType = "kubernetes.io/tls";
    private const string CertLabelKey = "kcert.dev/secret";
    public const string IngressLabelKey = "kcert.dev/ingress";
    private const string TlsTypeSelector = "type=kubernetes.io/tls";

    private string IngressLabel => $"{IngressLabelKey}={cfg.IngressLabelValue}";
    private static string ConfigMapLabel => $"{K8sWatchClient.CertRequestKey}={K8sWatchClient.CertRequestValue}";
    private string ManagedSecretLabel => $"{CertLabelKey}={cfg.IngressLabelValue}";
    private static string UnManagedSecretLabel => $"!{CertLabelKey}";

    private record ListResult<T>(string? Tok, IList<T> It);


    public IAsyncEnumerable<V1Ingress> GetAllIngressesAsync(CancellationToken tok)
    {
        return IterateAsync(GetAllIngressesAsync, GetNsIngressesAsync, tok);
    }

    private async Task<ListResult<V1Ingress>> GetAllIngressesAsync(string? c, CancellationToken tok)
    {
        var res = await client.ListIngressForAllNamespacesAsync(labelSelector: IngressLabel, continueParameter: c, cancellationToken: tok);
        return new ListResult<V1Ingress>(res.Continue(), res.Items);
    }

    private async Task<ListResult<V1Ingress>> GetNsIngressesAsync(string ns, string? cTok, CancellationToken tok)
    {
        var res = await client.ListNamespacedIngressAsync(ns, labelSelector: IngressLabel, continueParameter: cTok, cancellationToken: tok);
        return new ListResult<V1Ingress>(res.Continue(), res.Items);
    }

    public IAsyncEnumerable<V1ConfigMap> GetAllConfigMapsAsync(CancellationToken tok)
    {
        return IterateAsync(GetAllConfigMapsAsync, GetNsConfigMapsAsync, tok);
    }

    private async Task<ListResult<V1ConfigMap>> GetAllConfigMapsAsync(string? continuationToken, CancellationToken tok)
    {
        var res = await client.ListConfigMapForAllNamespacesAsync(labelSelector: ConfigMapLabel, continueParameter: continuationToken, cancellationToken: tok);
        return new(res.Continue(), res.Items);
    }
    private async Task<ListResult<V1ConfigMap>> GetNsConfigMapsAsync(string ns, string? tok, CancellationToken t)
    {
        var res = await client.ListNamespacedConfigMapAsync(ns, labelSelector: ConfigMapLabel, continueParameter: tok, cancellationToken: t);
        return new(res.Continue(), res.Items);
    }

    public IAsyncEnumerable<V1Secret> GetManagedSecretsAsync(CancellationToken tok)
    {
        return IterateAsync(GetAllManagedSecretsAsync, GetNsManagedSecretsAsync, tok);
    }

    private async Task<ListResult<V1Secret>> GetAllManagedSecretsAsync(string? tok, CancellationToken t)
    {
        var res = await client.ListSecretForAllNamespacesAsync(labelSelector: ManagedSecretLabel, continueParameter: tok, cancellationToken: t);
        return new(res.Continue(), res.Items);
    }


    private async Task<ListResult<V1Secret>> GetNsManagedSecretsAsync(string ns, string? tok, CancellationToken t)
    {
        var res = await client.ListNamespacedSecretAsync(ns, labelSelector: ManagedSecretLabel, continueParameter: tok, cancellationToken: t);
        return new(res.Continue(), res.Items);
    }

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

    public async Task<V1Ingress?> GetIngressAsync(string ns, string name, CancellationToken tok)
    {
        try
        {
            return await client.ReadNamespacedIngressAsync(name, ns, cancellationToken: tok);
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

    public async Task DeleteIngressAsync(string ns, string name, CancellationToken tok)
    {
        try
        {
            await client.DeleteNamespacedIngressAsync(name, ns, cancellationToken: tok);
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

    private delegate Task<ListResult<T>> ListAllFunc<T>(string? continueToken, CancellationToken tok);
    private delegate Task<ListResult<T>> ListNsFunc<T>(string ns, string? continueToken, CancellationToken tokt);

    private IAsyncEnumerable<T> IterateAsync<T>(ListAllFunc<T> all, ListNsFunc<T> byNs, CancellationToken tok)
    {
        return cfg.NamespaceConstraints.Length == 0
            ? IterateAsync(all, tok)
            : IterateAsync(byNs, tok);
    }

    private static async IAsyncEnumerable<T> IterateAsync<T>(ListAllFunc<T> callback, [EnumeratorCancellation] CancellationToken tok)
    {
        string? continueToken = null;
        do
        {
            var res = await callback(continueToken, tok);
            continueToken = res.Tok;
            foreach (var item in res.It)
            {
                yield return item;
            }
        } while (continueToken != null);
    }

    private async IAsyncEnumerable<T> IterateAsync<T>(ListNsFunc<T> callback, [EnumeratorCancellation] CancellationToken tok)
    {
        foreach (var ns in cfg.NamespaceConstraints)
        {
            await foreach (var item in IterateAsync((t, c) => callback(ns, t, c), tok))
            {
                yield return item;
            }
        }
    }
}
