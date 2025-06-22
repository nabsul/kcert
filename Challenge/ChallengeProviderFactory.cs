using KCert.Services;

namespace KCert.Challenge;

public class ChallengeProviderFactory(KCertConfig cfg, IServiceProvider svc)
{
    public IChallengeProvider? CreateProvider()
    {
        return cfg.ChallengeType switch
        {
            "route53" => svc.GetService<AwsRoute53Provider>(),
            "cloudflare" => svc.GetService<CloudflareProvider>(),
            _ => null,
        };
    }
}
