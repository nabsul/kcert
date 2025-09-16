namespace KCert.Challenge;

using k8s.Models;
using KCert.Models;
using KCert.Services;

public class HttpChallengeProvider(K8sClient kube, KCertConfig cfg, ILogger<HttpChallengeProvider> log, CertClient cert) : IChallengeProvider
{
    public string AcmeChallengeType => "http-01";

    private record HttpChallengeState(string[] Hosts);

    public string HandleChallenge(string token)
    {
        log.LogInformation("Received ACME Challenge: {token}", token);
        var thumbprint = cert.GetThumbprint();
        return $"{token}.{thumbprint}";
    }

    public async Task<object?> PrepareChallengesAsync(IEnumerable<AcmeAuthzResponse> auths, CancellationToken tok)
    {
        var hosts = auths.Select(auth => auth.Identifier.Value).ToArray();
        log.LogInformation("HTTP-01 challenge Ingress will be managed for hosts: {hosts}", string.Join(", ", hosts));
        await AddChallengeHostsAsync(hosts, tok);
        return new HttpChallengeState(hosts);
    }

    public async Task CleanupChallengeAsync(object? state, CancellationToken tok)
    {
        if (state is not HttpChallengeState { Hosts: var hosts })
        {
            throw new ArgumentException("Invalid state provided for HTTP challenge cleanup. Expected HttpChallengeState.", nameof(state));
        }

        log.LogInformation("Deleting HTTP challenge Ingress for hosts: {hosts}", string.Join(", ", hosts));
        await kube.DeleteIngressAsync(cfg.KCertNamespace, cfg.KCertIngressName, tok);
    }

    private async Task AddChallengeHostsAsync(IEnumerable<string> hosts, CancellationToken tok)
    {
        var kcertIngress = await kube.GetIngressAsync(cfg.KCertNamespace, cfg.KCertIngressName, tok);
        if (kcertIngress != null)
        {
            await kube.DeleteIngressAsync(cfg.KCertNamespace, cfg.KCertIngressName, tok);
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

        if (string.IsNullOrWhiteSpace(cfg.ChallengeIngressClassName) is false)
        {
            kcertIngress.Spec.IngressClassName = cfg.ChallengeIngressClassName;
        }

        kcertIngress.Metadata.Annotations = cfg.ChallengeIngressAnnotations;
        kcertIngress.Metadata.Labels = cfg.ChallengeIngressLabels;

        await kube.CreateIngressAsync(kcertIngress, tok);
        log.LogInformation("Giving challenge ingress time to propagate");

        if (!cfg.SkipIngressPropagationCheck)
        {
            await AwaitIngressPropagationAsync(kcertIngress, tok);
        }
        else
        {
            await Task.Delay(cfg.ChallengeIngressMaxPropagationWaitTime, tok);
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

    private async Task AwaitIngressPropagationAsync(V1Ingress kcertIngress, CancellationToken tok)
    {
        var timeoutCancellationToken = new CancellationTokenSource(cfg.ChallengeIngressMaxPropagationWaitTime).Token;
        while (timeoutCancellationToken.IsCancellationRequested is false)
        {
            if (await IsIngressPropagated(kcertIngress, tok)) return;

            await Task.Delay(cfg.ChallengeIngressPropagationCheckInterval, cancellationToken: timeoutCancellationToken);
        }

        throw new Exception(
            message:
            $"Ingress {kcertIngress.Name()}.{kcertIngress.Namespace()} was not propagated in time "
          + $"({nameof(KCertConfig.ChallengeIngressMaxPropagationWaitTime)}:{cfg.ChallengeIngressMaxPropagationWaitTime})");
    }

    private async Task<bool> IsIngressPropagated(V1Ingress kcertIngress, CancellationToken tok)
    {
        var ingress = await kube.GetIngressAsync(kcertIngress.Namespace(), kcertIngress.Name(), tok);
        return ingress?.Status.LoadBalancer.Ingress?.Any() ?? false;
    }
}
