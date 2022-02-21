using Microsoft.Extensions.Configuration;
using System;
using System.Linq;

namespace KCert.Services;

[Service]
public class KCertConfig
{
    private readonly IConfiguration _cfg;

    public KCertConfig(IConfiguration cfg)
    {
        _cfg = cfg;
    }

    public string K8sConfigFile => _cfg["Config"];
    public string KCertNamespace => GetString("Namespace");
    public string Label => GetString("Label");
    public string KCertSecretName => GetString("SecretName");
    public string KCertServiceName => GetString("ServiceName");
    public string KCertIngressName => GetString("IngressName");
    public int KCertServicePort => GetInt("ServicePort");

    public Uri AcmeDir => new(GetString("Acme:DirUrl"));
    public string AcmeEmail => GetString("Acme:Email");
    public string AcmeKey => GetString("Acme:Key");
    public bool AcmeAccepted => GetBool("Acme:TermsAccepted");

    public string SmtpEmailFrom => GetString("Smtp:EmailFrom");
    public string SmtpHost => GetString("Smtp:Host");
    public int SmtpPort => GetInt("Smtp:Port");
    public string SmtpUser => GetString("Smtp:User");
    public string SmtpPass => GetString("Smtp:Pass");

    public TimeSpan AcmeWaitTime => TimeSpan.FromSeconds(_cfg.GetValue<int>("AcmeWaitTimeSeconds"));
    public int AcmeNumRetries => _cfg.GetValue<int>("AcmeNumRetries");

    public bool EnableAutoRenew => GetBool("EnableAutoRenewal");
    public TimeSpan RenewalTimeBetweenChecks => TimeSpan.FromHours(_cfg.GetValue<int>("RenewalCheckTimeHours"));
    public TimeSpan RenewalExpirationLimit => TimeSpan.FromDays(_cfg.GetValue<int>("RenewalExpirationRenewalDays"));

    public object AllConfigs => new
    {
        KCert = new { KCertNamespace, KCertIngressName, KCertSecretName, KCertServiceName, KCertServicePort },
        ACME = new { AcmeAccepted, AcmeDir, AcmeEmail, AcmeKey = HideString(AcmeKey) },
        SMTP = new { SmtpEmailFrom, SmtpHost, SmtpPort, SmtpUser, SmtpPass = HideString(SmtpPass) },
    };

    private static string HideString(string val) => string.Join("", Enumerable.Repeat("*", val.Length));
    private string GetString(string key) => _cfg.GetValue<string>(key);
    private int GetInt(string key) => _cfg.GetValue<int>(key);
    private bool GetBool(string key) => _cfg.GetValue<bool>(key);
}
