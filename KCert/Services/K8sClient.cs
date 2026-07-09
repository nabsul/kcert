using k8s;
using k8s.Autorest;
using k8s.Models;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace KCert.Services;

public class K8sClient(KCertConfig cfg, Kubernetes client)
{
    private const string TlsSecretType = "kubernetes.io/tls";
    private const string CertLabelKey = "kcert.dev/secret";
    public const string IngressLabelKey = "kcert.dev/ingress";

    private string IngressLabel => $"{IngressLabelKey}={cfg.IngressLabelValue}";
    private static string ConfigMapLabel => $"{K8sWatchClient.CertRequestKey}={K8sWatchClient.CertRequestValue}";
    private string ManagedSecretLabel => $"{CertLabelKey}={cfg.IngressLabelValue}";

    public IAsyncEnumerable<V1Ingress> GetAllIngressesAsync(CancellationToken tok) => IterateAsync<V1Ingress, V1IngressList>(GetAllIngressesAsync, GetNsIngressesAsync, tok);    
    private Task<V1IngressList> GetAllIngressesAsync(string? c, CancellationToken tok) => client.ListIngressForAllNamespacesAsync(labelSelector: IngressLabel, continueParameter: c, cancellationToken: tok);
    private Task<V1IngressList> GetNsIngressesAsync(string ns, string? cTok, CancellationToken tok) => client.ListNamespacedIngressAsync(ns, labelSelector: IngressLabel, continueParameter: cTok, cancellationToken: tok);

    public IAsyncEnumerable<V1ConfigMap> GetAllConfigMapsAsync(CancellationToken tok) => IterateAsync<V1ConfigMap, V1ConfigMapList>(GetAllConfigMapsAsync, GetNsConfigMapsAsync, tok);
    private Task<V1ConfigMapList> GetAllConfigMapsAsync(string? continuationToken, CancellationToken tok) => client.ListConfigMapForAllNamespacesAsync(labelSelector: ConfigMapLabel, continueParameter: continuationToken, cancellationToken: tok);
    private Task<V1ConfigMapList> GetNsConfigMapsAsync(string ns, string? tok, CancellationToken t) => client.ListNamespacedConfigMapAsync(ns, labelSelector: ConfigMapLabel, continueParameter: tok, cancellationToken: t);

    public IAsyncEnumerable<V1Secret> GetManagedSecretsAsync(CancellationToken tok) => IterateAsync<V1Secret, V1SecretList>(GetAllManagedSecretsAsync, GetNsManagedSecretsAsync, tok);
    private Task<V1SecretList> GetAllManagedSecretsAsync(string? tok, CancellationToken t) => client.ListSecretForAllNamespacesAsync(labelSelector: ManagedSecretLabel, continueParameter: tok, cancellationToken: t);
    private Task<V1SecretList> GetNsManagedSecretsAsync(string ns, string? tok, CancellationToken t) => client.ListNamespacedSecretAsync(ns, labelSelector: ManagedSecretLabel, continueParameter: tok, cancellationToken: t);

    public async Task<V1Secret?> GetSecretAsync(string ns, string name, CancellationToken tok)
    {
        try
        {
            return await client.ReadNamespacedSecretAsync(name, ns, cancellationToken: tok);
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

    public async Task CreateIngressAsync(V1Ingress ingress, CancellationToken tok)
    {
        await client.CreateNamespacedIngressAsync(ingress, cfg.KCertNamespace, cancellationToken: tok);
    }

    public async Task UpdateTlsSecretAsync(string ns, string name, string key, string cert, CancellationToken tok)
    {
        var secret = await GetSecretAsync(ns, name, tok);
        var alreadyExists = false;
        if (secret != null)
        {
            // if it's a cert we can directly replace it
            if (secret.Type == TlsSecretType)
            {
                alreadyExists = true;
            }
            else
            {
                // if it's an opaque secret (ie: a request to create a cert) we delete it and create the cert
                await client.DeleteNamespacedSecretAsync(name, ns, cancellationToken: tok);
            }
        }

        if (secret == null || !alreadyExists)
        {
            secret = new V1Secret
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

        secret.Metadata.Labels ??= new Dictionary<string, string>();
        secret.Metadata.Labels[CertLabelKey] = cfg.IngressLabelValue;
        secret.Data["tls.key"] = Encoding.UTF8.GetBytes(key);
        secret.Data["tls.crt"] = Encoding.UTF8.GetBytes(cert);

        if (alreadyExists)
        {
            await client.ReplaceNamespacedSecretAsync(secret, name, ns, cancellationToken: tok);
        }
        else
        {
            await client.CreateNamespacedSecretAsync(secret, ns, cancellationToken: tok);
        }
    }

    private delegate Task<TT> ListAllFunc<T, TT>(string? continueToken, CancellationToken tok) where TT : IKubernetesObject<V1ListMeta>, IItems<T>;
    private delegate Task<TT> ListNsFunc<T, TT>(string ns, string? continueToken, CancellationToken tokt) where TT : IKubernetesObject<V1ListMeta>, IItems<T>;

    private IAsyncEnumerable<T> IterateAsync<T, TT>(ListAllFunc<T, TT> all, ListNsFunc<T, TT> byNs, CancellationToken tok) where TT : IKubernetesObject<V1ListMeta>, IItems<T>
    {
        return cfg.NamespaceConstraints.Length == 0
            ? IterateAsync(all, tok)
            : IterateAsync(byNs, tok);
    }

    private static async IAsyncEnumerable<T> IterateAsync<T, TT>(ListAllFunc<T, TT> callback, [EnumeratorCancellation] CancellationToken tok) where TT : IKubernetesObject<V1ListMeta>, IItems<T>
    {
        string? continueToken = null;
        do
        {
            var res = await callback(continueToken, tok);
            continueToken = res.Continue();
            foreach (var item in res.Items)
            {
                yield return item;
            }
        } while (continueToken != null);
    }

    private async IAsyncEnumerable<T> IterateAsync<T, TT>(ListNsFunc<T, TT> callback, [EnumeratorCancellation] CancellationToken tok) where TT : IKubernetesObject<V1ListMeta>, IItems<T>
    {
        foreach (var ns in cfg.NamespaceConstraints)
        {
            await foreach (var item in IterateAsync<T, TT>((t, c) => callback(ns, t, c), tok))
            {
                yield return item;
            }
        }
    }
}
