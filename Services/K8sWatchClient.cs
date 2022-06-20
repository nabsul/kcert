using k8s;
using k8s.Autorest;
using k8s.Exceptions;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Net.Http;
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

    public async Task WatchIngressesAsync(Func<WatchEventType, V1Ingress, Task> callback, CancellationToken tok)
    {
        var label = $"{IngressLabelKey}={IngressLabelValue}";
        var watch = () => _client.NetworkingV1.ListIngressForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: label);
        await WatchInLoopAsync(label, watch, callback);
    }

    public async Task WatchConfigMapsAsync(Func<WatchEventType, V1ConfigMap, Task> callback, CancellationToken tok)
    {
        var label = $"{CertRequestKey}={CertRequestValue}";
        var watch = () => _client.CoreV1.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: label);
        await WatchInLoopAsync(label, watch, callback);
    }

    private async Task WatchInLoopAsync<T, L>(string label, Func<Task<HttpOperationResponse<L>>> watch, Func<WatchEventType, T, Task> callback)
    {
        var typeName = typeof(T).Name;
        while (true)
        {
            try
            {
                _log.LogInformation("Starting watch request for {type}[{label}]", typeName, label);
                await foreach (var (type, item) in watch().WatchAsync<T, L>())
                {
                    await callback(type, item);
                }
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message == "Error while copying content to a stream.")
                {
                    _log.LogInformation("Empty Kubernetes client result threw an exception. Retrying {type}[{label}].", typeName, label);
                }
                else
                {
                    throw;
                }
            }
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
