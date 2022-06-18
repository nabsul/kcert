using k8s;
using k8s.Exceptions;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class K8sWatchClient
{
    private const string CertRequestKey = "kcert.dev/cert-request";
    private const string CertRequestValue = "request";
    public const string IngressLabelKey = "kcert.dev/ingress";
    public const string IngressLabelValue = "managed";

    private readonly KCertConfig _cfg;

    private readonly ILogger<K8sClient> _log;

    private readonly Kubernetes _client;

    public K8sWatchClient(KCertConfig cfg, ILogger<K8sClient> log)
    {
        _cfg = cfg;
        _log = log;
        _client = new Kubernetes(GetConfig());
    }

    public async Task WatchIngressesAsync(Func<WatchEventType, V1Ingress, CancellationToken, Task> callback, CancellationToken tok)
    {
        var label = $"{IngressLabelKey}={IngressLabelValue}";
        _log.LogInformation("Watching for all ingresses with: {label}", label);
        var message = _client.ListIngressForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: label);
        await foreach (var (type, item) in message.WatchAsync<V1Ingress, V1IngressList>())
        {
            await callback(type, item, tok);
        }
    }

    public async Task WatchConfigMapsAsync(Func<WatchEventType, V1ConfigMap, CancellationToken, Task> callback, CancellationToken tok)
    {
        var label = $"{CertRequestKey}={CertRequestValue}";
        _log.LogInformation("Watching for all secrets with: {label}", label);
        var message = _client.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: label);
        await foreach (var (type, item) in message.WatchAsync<V1ConfigMap, V1ConfigMapList>())
        {
            await callback(type, item, tok);
        }
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
