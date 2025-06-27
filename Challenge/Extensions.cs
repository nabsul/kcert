namespace KCert.Challenge;

using KCert.Config;

public static class Extensions
{
    public static IServiceCollection AddChallenge(this IServiceCollection services, KCertConfig cfg)
    {
        services.AddSingleton<DnsUtils>();

        return cfg.ChallengeType switch
        {
            "http" => services.AddSingleton<IChallengeProvider, HttpChallengeProvider>(),
            "route53" => services.AddSingleton<IChallengeProvider, AwsRoute53Provider>(),
            "cloudflare" => services.AddSingleton<IChallengeProvider, CloudflareProvider>(),
            _ => throw new NotSupportedException($"Challenge type '{cfg.ChallengeType}' is not supported.")
        };
    }
}
