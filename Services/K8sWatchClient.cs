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
        return WatchInLoopAsync(callback, WatchAllIngressAsync, WatchNsIngressAsync, tok);
    }

    private Task<HttpOperationResponse<V1IngressList>> WatchAllIngressAsync(CancellationToken tok)
    {
        return client.NetworkingV1.ListIngressForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: IngressLabel);
    }

    private Task<HttpOperationResponse<V1IngressList>> WatchNsIngressAsync(string ns, CancellationToken tok)
    {
        return client.NetworkingV1.ListNamespacedIngressWithHttpMessagesAsync(ns, watch: true, cancellationToken: tok, labelSelector: IngressLabel);
    }

    public Task WatchConfigMapsAsync(ChangeCallback<V1ConfigMap> callback, CancellationToken tok)
    {
        return WatchInLoopAsync(callback, WatchAllConfigMapsAsync, WatchNsConfigMapsAsync, tok);
    }

    private Task<HttpOperationResponse<V1ConfigMapList>> WatchAllConfigMapsAsync(CancellationToken tok)
    {
        return client.CoreV1.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: ConfigLabel);
    }

    private Task<HttpOperationResponse<V1ConfigMapList>> WatchNsConfigMapsAsync(string ns, CancellationToken tok)
    {
        return client.CoreV1.ListNamespacedConfigMapWithHttpMessagesAsync(ns, watch: true, cancellationToken: tok, labelSelector: ConfigLabel);
    }

    delegate Task<HttpOperationResponse<L>> WatchAllFunc<L>(CancellationToken tok);
    delegate Task<HttpOperationResponse<L>> WatchNsFunc<L>(string ns, CancellationToken tok);

    private Task WatchInLoopAsync<L, T>(ChangeCallback<T> callback, WatchAllFunc<L> all, WatchNsFunc<L> ns, CancellationToken tok)
    {
        return cfg.NamespaceConstraints.Length == 0 ? WatchInLoopAsync(typeof(T).Name, callback, all, tok) : WatchInLoopAsync(callback, ns, tok);
    }

    private Task WatchInLoopAsync<L, T>(ChangeCallback<T> callback, WatchNsFunc<L> func, CancellationToken tok)
    {
        var tasks = cfg.NamespaceConstraints.Select(ns => WatchInLoopAsync($"{ns}:{typeof(T).Name}", callback, (t) => func(ns, t), tok));
        return Task.WhenAll([..tasks]);
    }

    private async Task WatchInLoopAsync<L, T>(string id, ChangeCallback<T> callback, WatchAllFunc<L> watch, CancellationToken tok)
    {
        var typeName = typeof(T).Name;
        while (true)
        {
            try
            {
                await foreach (var (type, item) in watch(tok).WatchAsync<T, L>())
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
