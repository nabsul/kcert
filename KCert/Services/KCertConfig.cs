namespace KCert.Services;

public class KCertConfig(IConfiguration cfg)
{
    private readonly string _backupKey = CertClient.GenerateNewKey();

    public bool WatchIngresses => GetBool("KCert:WatchIngresses");
    public bool WatchConfigMaps => GetBool("KCert:WatchConfigMaps");
    public string KCertNamespace => GetRequiredString("KCert:Namespace");
    public string KCertSecretName => GetRequiredString("KCert:SecretName");
    public string KCertServiceName => GetRequiredString("KCert:ServiceName");
    public string KCertIngressName => GetRequiredString("KCert:IngressName");
    public int KCertServicePort => GetInt("KCert:ServicePort");
    public bool ShowRenewButton => GetBool("KCert:ShowRenewButton");
    public string[] NamespaceConstraints => GetString("KCert:NamespaceConstraints")?.Split(",") ?? [];

    public string ChallengeIngressClassName => GetRequiredString("ChallengeIngress:ClassName");

    public Dictionary<string, string> ChallengeIngressAnnotations => GetDictionary("ChallengeIngress:Annotations");

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
    public string AcmeKey => cfg.GetValue("Acme:Key", _backupKey);
    public bool AcmeAccepted => GetBool("Acme:TermsAccepted");

    public bool UseEabKey => !string.IsNullOrEmpty(AcmeEabKeyId);
    public string AcmeEabKeyId => GetRequiredString("Acme:EabKeyId");
    public string AcmeHmacKey => GetRequiredString("Acme:EabHmacKey");

    public bool SmtpEnabled => !string.IsNullOrEmpty(SmtpEmailFrom);
    public string SmtpEmailFrom => GetRequiredString("Smtp:EmailFrom");
    public string SmtpHost => GetRequiredString("Smtp:Host");
    public int SmtpPort => GetInt("Smtp:Port");
    public string SmtpUser => GetRequiredString("Smtp:User");
    public string SmtpPass => GetRequiredString("Smtp:Pass");

    public string IngressLabelValue => GetRequiredString("ChallengeIngress:IngressLabelValue");

    public string Route53AccessKeyId => GetRequiredString("Route53:AccessKeyId");
    public string Route53SecretAccessKey => GetRequiredString("Route53:SecretAccessKey");
    public string Route53Region => GetRequiredString("Route53:Region");

    public string CloudflareApiToken => GetRequiredString("Cloudflare:ApiToken");
    public string CloudflareAccountId => GetRequiredString("Cloudflare:AccountId");

    public string ChallengeType => GetRequiredString("KCert:ChallengeType");

    public object AllConfigs
    {
        get
        {
            var res = new Dictionary<string, object>();
            var props = typeof(KCertConfig).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)
                .Where(p => p.Name != nameof(AllConfigs)).OrderBy(p => p.Name);

            foreach (var prop in props)
            {
                string[] redactTerms = ["Key", "Pass", "Token", "Secret"];
                bool redact = redactTerms.Any(term => prop.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
                var val = prop.GetValue(this);
                res[prop.Name] = redact ? HideString(val?.ToString()) : val ?? "[NOT CONFIGURED]";
            }
            return res;
        }
    }

    private static string HideString(string? val) => string.IsNullOrEmpty(val) ? "" : "[REDACTED]";
    private string? GetString(string key) => cfg.GetValue<string>(key);
    private string GetRequiredString(string key) => cfg.GetValue<string>(key) ?? throw new Exception($"[{key}] must be defined");
    private int GetInt(string key) => cfg.GetValue<int>(key);
    private bool GetBool(string key) => cfg.GetValue<bool>(key);

    private Dictionary<string, string> GetDictionary(string key)
    {
        var data = cfg.GetSection(key)?.GetChildren() ?? [];
        return data.ToDictionary(s => s.Key, s => s.Value ?? "");
    }
}
