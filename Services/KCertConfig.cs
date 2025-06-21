namespace KCert.Services;

[Service]
public class KCertConfig(IConfiguration cfg)
{
    private readonly string _key = CertClient.GenerateNewKey();

    public bool WatchIngresses => GetBool("KCert:WatchIngresses");
    public bool WatchConfigMaps => GetBool("KCert:WatchConfigMaps");
    public string? K8sConfigFile => cfg["Config"];
    public string KCertNamespace => GetRequiredString("KCert:Namespace");
    public string KCertSecretName => GetRequiredString("KCert:SecretName");
    public string KCertServiceName => GetRequiredString("KCert:ServiceName");
    public string KCertIngressName => GetRequiredString("KCert:IngressName");
    public int KCertServicePort => GetInt("KCert:ServicePort");
    public bool ShowRenewButton => GetBool("KCert:ShowRenewButton");
    public int InitialSleepOnFailure => GetInt("KCert:InitialSleepOnFailure");
    public string[] NamespaceConstraints => GetString("KCert:NamespaceConstraints")?.Split(",") ?? [];

    public bool UseChallengeIngressClassName => GetBool("ChallengeIngress:UseClassName");
    public string ChallengeIngressClassName => GetRequiredString("ChallengeIngress:ClassName");

    public bool UseChallengeIngressAnnotations => GetBool("ChallengeIngress:UseAnnotations");
    public Dictionary<string, string> ChallengeIngressAnnotations => GetDictionary("ChallengeIngress:Annotations");

    public bool UseChallengeIngressLabels => GetBool("ChallengeIngress:UseLabels");
    public Dictionary<string, string> ChallengeIngressLabels => GetDictionary("ChallengeIngress:Labels");

    public TimeSpan ChallengeIngressMaxPropagationWaitTime => TimeSpan.FromSeconds(GetInt("ChallengeIngress:MaxPropagationWaitTimeSeconds"));
    public TimeSpan ChallengeIngressPropagationCheckInterval => TimeSpan.FromMilliseconds(GetInt("ChallengeIngress:PropagationCheckIntervalMilliseconds"));
    public bool SkipIngressPropagationCheck => GetBool("ChallengeIngress:SkipIngressPropagationCheck");

    public TimeSpan AcmeWaitTime => TimeSpan.FromSeconds(cfg.GetValue<int>("Acme:ValidationWaitTimeSeconds"));
    public int AcmeNumRetries => cfg.GetValue<int>("Acme:ValidationNumRetries");
    public bool EnableAutoRenew => GetBool("Acme:AutoRenewal");
    public TimeSpan RenewalTimeBetweenChecks => TimeSpan.FromHours(cfg.GetValue<int>("Acme:RenewalCheckTimeHours"));
    public TimeSpan RenewalExpirationLimit => TimeSpan.FromDays(cfg.GetValue<int>("Acme:RenewalThresholdDays"));

    public Uri AcmeDir => new(GetRequiredString("Acme:DirUrl"));
    public string AcmeEmail => GetRequiredString("Acme:Email");
    public string AcmeKey => GetString("Acme:Key") ?? _key; // If no key is provided via configs, use generated key.
    public bool AcmeAccepted => GetBool("Acme:TermsAccepted");

    public string? AcmeEabKeyId => GetString("Acme:EabKeyId");
    public string? AcmeHmacKey => GetString("Acme:EabHmacKey");

    public string? SmtpEmailFrom => GetString("Smtp:EmailFrom");
    public string? SmtpHost => GetString("Smtp:Host");
    public int SmtpPort => GetInt("Smtp:Port");
    public string? SmtpUser => GetString("Smtp:User");
    public string? SmtpPass => GetString("Smtp:Pass");

    public string IngressLabelValue => GetRequiredString("ChallengeIngress:IngressLabelValue");

    public string ConfigMapWatchLabelKey { get; } = Environment.GetEnvironmentVariable("KCERT_CONFIGMAP_WATCH_LABEL_KEY") ?? "kcert.dev/configmap";
    public string ConfigMapWatchLabelValue { get; } = Environment.GetEnvironmentVariable("KCERT_CONFIGMAP_WATCH_LABEL_VALUE") ?? "";

    // AWS Route53 Configuration
    public bool EnableRoute53 => GetBool("KCert:Route53:EnableRoute53");
    public string Route53AccessKeyId => GetRequiredString("KCert:Route53:AccessKeyId");
    public string Route53SecretAccessKey => GetRequiredString("KCert:Route53:SecretAccessKey");
    public string Route53Region => GetRequiredString("KCert:Route53:Region");

    // Cloudflare Configuration
    public bool EnableCloudflare => GetBool("KCert:Cloudflare:EnableCloudflare");
    public string CloudflareApiToken => GetRequiredString("KCert:Cloudflare:ApiToken");
    public string CloudflareAccountId => GetRequiredString("KCert:Cloudflare:AccountId");

    // Preferred Challenge Type
    public string PreferredChallengeType => GetString("KCert:PreferredChallengeType") ?? "http-01";

    public object AllConfigs => new
    {
        KCert = new
        {
            Namespace = KCertNamespace,
            IngressName = KCertIngressName,
            SecertName = KCertSecretName,
            ServiceName = KCertServiceName,
            ServicePort = KCertServicePort,
            ShowRenewButton,
            NamespaceConstraints,
            PreferredChallengeType = PreferredChallengeType,
        },
        ACME = new
        {
            ValidationWaitTimeSeconds = AcmeWaitTime,
            ValidationNumRetries = AcmeNumRetries,
            AutoRenewal = EnableAutoRenew,
            RenewalCheckTimeHours = RenewalTimeBetweenChecks,
            RenewalThresholdDays = RenewalExpirationLimit,
            TermsAccepted = AcmeAccepted,
            DirUrl = AcmeDir,
            Email = HideString(AcmeEmail),
            Key = HideString(AcmeKey)
        },
        SMTP = new
        {
            EmailFrom = HideString(SmtpEmailFrom),
            Host = HideString(SmtpHost),
            Port = SmtpPort,
            User = HideString(SmtpUser),
            Pass = HideString(SmtpPass)
        },
        Route53 = new
        {
            AccessKeyId = GetString("KCert:Route53:AccessKeyId"),
            SecretAccessKey = HideString(GetString("KCert:Route53:SecretAccessKey")),
            Region = Route53Region,
        },
        Cloudflare = new
        {
            ApiToken = HideString(CloudflareApiToken),
            AccountId = CloudflareAccountId,
        }
    };

    private static string HideString(string? val) => string.IsNullOrEmpty(val) ? "" : "[REDACTED]";
    private string? GetString(string key) => cfg.GetValue<string>(key);
    private string GetRequiredString(string key) => cfg.GetValue<string>(key) ?? throw new Exception($"[{key}] must be defined");
    private int GetInt(string key) => cfg.GetValue<int>(key);
    private bool GetBool(string key) => cfg.GetValue<bool>(key);

    private Dictionary<string, string> GetDictionary(string key)
    {
        var data = cfg.GetSection(key)?.GetChildren() ?? Enumerable.Empty<IConfigurationSection>();
        return data.ToDictionary(s => s.Key, s => s.Value ?? "");
    }
}
