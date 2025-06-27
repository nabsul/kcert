namespace KCert.Services;

public static class Extensions
{
    public static IServiceCollection AddKCertServices(this IServiceCollection services)
    {
        return services
            .AddSingleton<AcmeClient>()
            .AddSingleton<CertChangeService>()
            .AddSingleton<CertClient>()
            .AddSingleton<ConfigMonitorService>()
            .AddSingleton<EmailClient>()
            .AddSingleton<ExponentialBackoff>()
            .AddSingleton<IngressMonitorService>()
            .AddSingleton<K8sClient>()
            .AddSingleton<K8sWatchClient>()
            .AddSingleton<KCertClient>()
            .AddSingleton<RenewalHandler>()
            .AddSingleton<RenewalService>();
    }
}
