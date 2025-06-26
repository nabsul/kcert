using k8s.Models;
using KCert.Services;

namespace KCert.Challenge;

public class HttpChallengeProvider(K8sClient kube, KCertConfig cfg, ILogger<HttpChallengeProvider> log) : IChallengeProvider
{
    public string AcmeChallengeType => "http-01";

    public async Task<object?> PrepareChallengeAsync(string[] hosts, CancellationToken tok)
    {
        log.LogInformation("HTTP-01 challenge Ingress will be managed for hosts: {hosts}", string.Join(", ", hosts));
        await AddChallengeHostsAsync(hosts);
        return new { Hosts = hosts };
    }

    public async Task CleanupChallengeAsync(object? state, CancellationToken tok)
    {
        var hosts = (string[])((dynamic)state!).Hosts;
        log.LogInformation("Deleting HTTP challenge Ingress for hosts: {hosts}", string.Join(", ", hosts));
        await kube.DeleteIngressAsync(cfg.KCertNamespace, cfg.KCertIngressName);
    }

    private async Task AddChallengeHostsAsync(IEnumerable<string> hosts)
    {
        var kcertIngress = await kube.GetIngressAsync(cfg.KCertNamespace, cfg.KCertIngressName);
        if (kcertIngress != null)
        {
            await kube.DeleteIngressAsync(cfg.KCertNamespace, cfg.KCertIngressName);
        }

        kcertIngress = new()
        {
            Metadata = new()
            {
                Name = cfg.KCertIngressName,
                NamespaceProperty = cfg.KCertNamespace,
            },
            Spec = new()
            {
                Rules = [.. hosts.Select(CreateRule)]
            }
        };

        if (cfg.UseChallengeIngressClassName)
        {
            kcertIngress.Spec.IngressClassName = cfg.ChallengeIngressClassName;
        }

        if (cfg.UseChallengeIngressAnnotations)
        {
            kcertIngress.Metadata.Annotations = cfg.ChallengeIngressAnnotations;
        }

        if (cfg.UseChallengeIngressLabels)
        {
            kcertIngress.Metadata.Labels = cfg.ChallengeIngressLabels;
        }

        await kube.CreateIngressAsync(kcertIngress);
        log.LogInformation("Giving challenge ingress time to propagate");

        if (!cfg.SkipIngressPropagationCheck)
        {
            await AwaitIngressPropagationAsync(kcertIngress);
        }
        else
        {
            await Task.Delay(cfg.ChallengeIngressMaxPropagationWaitTime);
        }
    }

    private V1IngressRule CreateRule(string host)
    {
        var path = new V1HTTPIngressPath
        {
            Path = "/.well-known/acme-challenge/",
            PathType = "Prefix",
            Backend = new()
            {
                Service = new()
                {
                    Name = cfg.KCertServiceName,
                    Port = new(number: cfg.KCertServicePort)
                },
            },
        };

        return new()
        {
            Host = host,
            Http = new()
            {
                Paths = new List<V1HTTPIngressPath>() { path }
            },
        };
    }

    private async Task AwaitIngressPropagationAsync(V1Ingress kcertIngress)
    {
        var timeoutCancellationToken = new CancellationTokenSource(cfg.ChallengeIngressMaxPropagationWaitTime).Token;
        while (timeoutCancellationToken.IsCancellationRequested is false)
        {
            if (await IsIngressPropagated(kcertIngress)) return;

            await Task.Delay(cfg.ChallengeIngressPropagationCheckInterval, cancellationToken: timeoutCancellationToken);
        }

        throw new Exception(
            message:
            $"Ingress {kcertIngress.Name()}.{kcertIngress.Namespace()} was not propagated in time "
          + $"({nameof(KCertConfig.ChallengeIngressMaxPropagationWaitTime)}:{cfg.ChallengeIngressMaxPropagationWaitTime})");
    }

    private async Task<bool> IsIngressPropagated(V1Ingress kcertIngress)
    {
        var ingress = await kube.GetIngressAsync(kcertIngress.Namespace(), kcertIngress.Name());
        return ingress?.Status.LoadBalancer.Ingress?.Any() ?? false;
    }
}
