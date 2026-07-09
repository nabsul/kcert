namespace KCert.Services;

public interface IKCertConfig
{
    bool WatchIngresses { get; }
    bool WatchConfigMaps { get; }
    string KCertNamespace { get; }
    string KCertSecretName { get; }
    string KCertServiceName { get; }
    string KCertIngressName { get; }
    int KCertServicePort { get; }
    bool ShowRenewButton { get; }
    string[] NamespaceConstraints { get; }
    string ChallengeIngressClassName { get; }
    Dictionary<string, string> ChallengeIngressAnnotations { get; }
    Dictionary<string, string> ChallengeIngressLabels { get; }
    TimeSpan ChallengeIngressMaxPropagationWaitTime { get; }
    TimeSpan ChallengeIngressPropagationCheckInterval { get; }
    bool SkipIngressPropagationCheck { get; }
    TimeSpan AcmeWaitTime { get; }
    int AcmeNumRetries { get; }
    bool EnableAutoRenew { get; }
    TimeSpan RenewalTimeBetweenChecks { get; }
    TimeSpan RenewalExpirationLimit { get; }
    Uri AcmeDir { get; }
    string AcmeEmail { get; }
    string AcmeKey { get; }
    bool AcmeAccepted { get; }
    bool UseEabKey { get; }
    string AcmeEabKeyId { get; }
    string AcmeHmacKey { get; }
    bool SmtpEnabled { get; }
    string SmtpEmailFrom { get; }
    string SmtpHost { get; }
    int SmtpPort { get; }
    string SmtpUser { get; }
    string SmtpPass { get; }
    string IngressLabelValue { get; }
    string Route53AccessKeyId { get; }
    string Route53SecretAccessKey { get; }
    string Route53Region { get; }
    string CloudflareApiToken { get; }
    string CloudflareAccountId { get; }
    string ChallengeType { get; }
    object AllConfigs { get; }
}
