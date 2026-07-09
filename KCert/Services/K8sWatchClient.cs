using k8s;
using k8s.Autorest;
using k8s.Models;

namespace KCert.Services;

public class K8sWatchClient(KCertConfig cfg, ILogger<K8sClient> log, Kubernetes client)
{
    public const string CertRequestKey = "kcert.dev/cert-request";
    public const string CertRequestValue = "request";
    public const string IngressLabelKey = "kcert.dev/ingress";

    public string IngressLabel => $"{IngressLabelKey}={cfg.IngressLabelValue}";
    public string ConfigLabel => $"{CertRequestKey}={CertRequestValue}";

    public delegate Task ChangeCallback<T>(WatchEventType type, T item, CancellationToken tok);

    public Task WatchIngressesAsync(ChangeCallback<V1Ingress> callback, CancellationToken tok)
    {
        IAsyncEnumerable<(WatchEventType, V1Ingress)> all(CancellationToken t) => client.NetworkingV1.WatchListIngressForAllNamespacesAsync(cancellationToken: t);
        IAsyncEnumerable<(WatchEventType, V1Ingress)> ns(string ns, CancellationToken t) => client.NetworkingV1.WatchListNamespacedIngressAsync(ns, cancellationToken: t);
        return WatchInLoopAsync(callback, all, ns, tok);
    }

    public Task WatchConfigMapsAsync(ChangeCallback<V1ConfigMap> callback, CancellationToken tok)
    {
        IAsyncEnumerable<(WatchEventType, V1ConfigMap)> all(CancellationToken t) => client.CoreV1.WatchListConfigMapForAllNamespacesAsync(cancellationToken: t);
        IAsyncEnumerable<(WatchEventType, V1ConfigMap)> ns(string ns, CancellationToken t) => client.CoreV1.WatchListNamespacedConfigMapAsync(ns, cancellationToken: t);
        return WatchInLoopAsync(callback, all, ns, tok);
    }

    delegate IAsyncEnumerable<(WatchEventType, T)> WatchAllFunc<T>(CancellationToken tok);
    delegate IAsyncEnumerable<(WatchEventType, T)> WatchNsFunc<T>(string ns, CancellationToken tok);

    private Task WatchInLoopAsync<T>(ChangeCallback<T> callback, WatchAllFunc<T> all, WatchNsFunc<T> ns, CancellationToken tok)
    {
        return cfg.NamespaceConstraints.Length == 0 ? WatchInLoopAsync(typeof(T).Name, callback, all, tok) : WatchInLoopAsync(callback, ns, tok);
    }

    private Task WatchInLoopAsync<T>(ChangeCallback<T> callback, WatchNsFunc<T> func, CancellationToken tok)
    {
        var tasks = cfg.NamespaceConstraints.Select(ns => WatchInLoopAsync($"{ns}:{typeof(T).Name}", callback, (t) => func(ns, t), tok));
        return Task.WhenAll([..tasks]);
    }

    private async Task WatchInLoopAsync<T>(string id, ChangeCallback<T> callback, WatchAllFunc<T> watch, CancellationToken tok)
    {
        var typeName = typeof(T).Name;
        while (true)
        {
            try
            {
                await foreach (var (type, item) in watch(tok))
                {
                    await callback(type, item, tok);
                }
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message == "Error while copying content to a stream.")
                {
                    log.LogInformation("Empty Kubernetes client result threw an exception watching [{id}]. Trying again after 5 seconds.", id);
                    await Task.Delay(TimeSpan.FromSeconds(5), tok);
                }
                else
                {
                    log.LogError("Unexpected error watching [{id}]", id);
                    throw;
                }
            }
        }
    }
}
