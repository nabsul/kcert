using k8s;
using k8s.Autorest;
using k8s.Exceptions;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class K8sWatchClient
{
    public const string CertRequestKey = "kcert.dev/cert-request";
    public const string CertRequestValue = "request";
    public const string IngressLabelKey = "kcert.dev/ingress";

    private readonly KCertConfig _cfg;

    private readonly ILogger<K8sClient> _log;

    private readonly Kubernetes _client;
    
    // In case of error in the watch loop, force all the watch services to restart by setting this global flag to true
    private bool watchExceptionLoop = false;

    private bool _namespaceConstraigned;

    public K8sWatchClient(KCertConfig cfg, ILogger<K8sClient> log)
    {
        _cfg = cfg;
        _log = log;
        _client = new Kubernetes(GetConfig());

        _namespaceConstraigned = _cfg.NamespaceConstraintsList.Count > 0;
    }

    public async Task WatchIngressesAsync(Func<WatchEventType, V1Ingress, Task> callback, CancellationToken tok)
    {
        var label = $"{IngressLabelKey}={_cfg.IngressLabelValue}";

        IEnumerable<Task<HttpOperationResponse<V1IngressList>>> taskList;

        if(_namespaceConstraigned)
        {
            taskList = _cfg.NamespaceConstraintsList.Select(ns => _client.NetworkingV1.ListNamespacedIngressWithHttpMessagesAsync(ns, watch: true, cancellationToken: tok, labelSelector: label));
        }
        else
        {
            taskList = new [] {_client.NetworkingV1.ListIngressForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: label)};
        }

        // Create the Func from the Tasks list
        var watchers = taskList.Select(t => WatchInLoopAsync(label, () => t, callback));
        
        await Task.WhenAll(watchers);
    }

    public async Task WatchConfigMapsAsync(Func<WatchEventType, V1ConfigMap, Task> callback, CancellationToken tok)
    {
        var label = $"{CertRequestKey}={CertRequestValue}";


        IEnumerable<Task<HttpOperationResponse<V1ConfigMapList>>> taskList;

        if(_namespaceConstraigned)
        {
            _log.LogInformation("Starting in namespaced mode and listening for the following namespaces: {ns}", String.Join("; ", _cfg.NamespaceConstraintsList));
            taskList = _cfg.NamespaceConstraintsList.Select(ns => _client.CoreV1.ListNamespacedConfigMapWithHttpMessagesAsync(ns, watch: true, cancellationToken: tok, labelSelector: label));
        }
        else
        {
            taskList = new [] {_client.CoreV1.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: label)};
        }


        // Create the Func from the Tasks list
        var watchers = taskList.Select(t => WatchInLoopAsync(label, () => t, callback));

        this.watchExceptionLoop = false;
        await Task.WhenAll(watchers);
    }

    private async Task WatchInLoopAsync<T, L>(string label, Func<Task<HttpOperationResponse<L>>> watch, Func<WatchEventType, T, Task> callback)
    {
        var typeName = typeof(T).Name;
        while (!watchExceptionLoop)
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
                    _log.LogInformation("Empty Kubernetes client result threw an exception");
                    _log.LogInformation("Restarting watch service.");

                    watchExceptionLoop = true;
                }
                else
                {
                    throw;
                }
            }
        }
    }

    private IEnumerable<Task<HttpOperationResponse<V1IngressList>>> GetNamespacedIngressListWithHttpMessagesAsync(List<string> namespaces, string label, CancellationToken tok)
    {
        var toWatch = namespaces.Select(async ns => await _client.NetworkingV1.ListNamespacedIngressWithHttpMessagesAsync(ns, watch: true, cancellationToken: tok, labelSelector: label));

        return toWatch;
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
