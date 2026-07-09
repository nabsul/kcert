using k8s;
using KCert.Challenge;
using KCert.Services;

namespace KCert;

public static class Extensions
{
    private static readonly Dictionary<string, Type> ChallengeTypes = new()
    {
        { "http", typeof(HttpChallengeProvider) },
        { "route53", typeof(AwsRoute53Provider) },
        { "cloudflare", typeof(CloudflareProvider) }
    };

    public static IServiceCollection AddKCertServices(this IServiceCollection services, KCertConfig cfg)
    {
        if (!ChallengeTypes.TryGetValue(cfg.ChallengeType, out var challengeType))
        {
            throw new NotSupportedException($"Challenge type '{cfg.ChallengeType}' is not supported.");
        }

        return services
            .AddSingleton<DnsUtils>()
            .AddSingleton(challengeType)
            .AddSingleton(typeof(IChallengeProvider), s => s.GetRequiredService(challengeType))
            .AddSingleton(cfg)
            .AddSingleton(new Kubernetes(KubernetesClientConfiguration.BuildDefaultConfig()))
            .AddTransient<AcmeClient>()
            .AddTransient<RenewalHandler>()
            .AddSingleton<CertChangeService>()
            .AddSingleton<CertClient>()
            .AddSingleton<ConfigMonitorService>()
            .AddSingleton<EmailClient>()
            .AddSingleton<IngressMonitorService>()
            .AddSingleton<K8sClient>()
            .AddSingleton<K8sWatchClient>()
            .AddSingleton<KCertClient>()
            .AddSingleton<RenewalService>()
            .AddHostedService<RenewalService>()
            .AddHostedService<IngressMonitorService>()
            .AddHostedService<ConfigMonitorService>();
    }
}
