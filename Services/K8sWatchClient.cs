using k8s;
using k8s.Autorest;
using k8s.Exceptions;
using k8s.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class K8sWatchClient(KCertConfig cfg, ILogger<K8sClient> log, Kubernetes client)
{
    public const string CertRequestKey = "kcert.dev/cert-request";
    public const string CertRequestValue = "request";
    public const string IngressLabelKey = "kcert.dev/ingress";

    // In case of error in the watch loop, force all the watch services to restart by setting this global flag to true
    private bool watchExceptionLoop = false;

    public async Task WatchIngressesAsync(Func<WatchEventType, V1Ingress, Task> callback, CancellationToken tok)
    {
        var label = $"{IngressLabelKey}={cfg.IngressLabelValue}";

        IEnumerable<Task<HttpOperationResponse<V1IngressList>>> taskList;

        if(cfg.NamespaceConstraints.Length > 0)
        {
            taskList = cfg.NamespaceConstraints.Select(ns => client.NetworkingV1.ListNamespacedIngressWithHttpMessagesAsync(ns, watch: true, cancellationToken: tok, labelSelector: label));
        }
        else
        {
            taskList = new [] {client.NetworkingV1.ListIngressForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: label)};
        }

        // Create the Func from the Tasks list
        var watchers = taskList.Select(t => WatchInLoopAsync(label, () => t, callback));
        
        await Task.WhenAll(watchers);
    }

    public async Task WatchConfigMapsAsync(Func<WatchEventType, V1ConfigMap, Task> callback, CancellationToken tok)
    {
        var label = $"{CertRequestKey}={CertRequestValue}";


        IEnumerable<Task<HttpOperationResponse<V1ConfigMapList>>> taskList;

        if(cfg.NamespaceConstraints.Length > 0)
        {
            log.LogInformation("Starting in namespaced mode and listening for the following namespaces: {ns}", String.Join("; ", cfg.NamespaceConstraints));
            taskList = cfg.NamespaceConstraints.Select(ns => client.CoreV1.ListNamespacedConfigMapWithHttpMessagesAsync(ns, watch: true, cancellationToken: tok, labelSelector: label));
        }
        else
        {
            taskList = new [] {client.CoreV1.ListConfigMapForAllNamespacesWithHttpMessagesAsync(watch: true, cancellationToken: tok, labelSelector: label)};
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
                log.LogInformation("Starting watch request for {type}[{label}]", typeName, label);
                await foreach (var (type, item) in watch().WatchAsync<T, L>())
                {
                    await callback(type, item);
                }
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message == "Error while copying content to a stream.")
                {
                    log.LogInformation("Empty Kubernetes client result threw an exception");
                    log.LogInformation("Restarting watch service.");

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
        var toWatch = namespaces.Select(async ns => await client.NetworkingV1.ListNamespacedIngressWithHttpMessagesAsync(ns, watch: true, cancellationToken: tok, labelSelector: label));

        return toWatch;
    }
}
