using KCert.Services;

namespace KCert.Challenge;

public class ChallengeProviderFactory(KCertConfig cfg, IServiceProvider svc)
{
    public IChallengeProvider CreateProvider()
    {
        return cfg.ChallengeType switch
        {
            "route53" => svc.GetRequiredService<AwsRoute53Provider>(),
            "cloudflare" => svc.GetRequiredService<CloudflareProvider>(),
            "http" => svc.GetRequiredService<HttpChallengeProvider>(),
            _ => throw new NotSupportedException($"Challenge type '{cfg.ChallengeType}' is not supported."),
        };
    }
}
