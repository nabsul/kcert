using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KCert.Services;

[Service]
public class KCertConfig
{
    private readonly IConfiguration _cfg;
    private readonly string _key;

    public KCertConfig(IConfiguration cfg)
    {
        _cfg = cfg;
        _key = CertClient.GenerateNewKey();
    }

    public bool WatchIngresses => GetBool("KCert:WatchIngresses");
    public bool WatchConfigMaps => GetBool("KCert:WatchConfigMaps");
    public string K8sConfigFile => _cfg["Config"];
    public string KCertNamespace => GetString("KCert:Namespace");
    public string KCertSecretName => GetString("KCert:SecretName");
    public string KCertServiceName => GetString("KCert:ServiceName");
    public string KCertIngressName => GetString("KCert:IngressName");
    public int KCertServicePort => GetInt("KCert:ServicePort");
    public bool ShowRenewButton => GetBool("KCert:ShowRenewButton");
    public int InitialSleepOnFailure => GetInt("KCert:InitialSleepOnFailure");
    public List<string> NamespaceConstraintsList => GetString("KCert:NamespaceConstraintsList").Split(";").ToList();
    public TimeSpan HttpTimeout => TimeSpan.FromSeconds(GetOptionalInt("KCert:HttpTimeoutSeconds") ?? 60);
    public bool EnableHttpRetry => GetOptionalBool("KCert:EnableHttpRetry") ?? false;
    

    public bool UseChallengeIngressClassName => GetBool("ChallengeIngress:UseClassName");
    public string ChallengeIngressClassName => GetString("ChallengeIngress:ClassName");

    public bool UseChallengeIngressAnnotations => GetBool("ChallengeIngress:UseAnnotations");
    public Dictionary<string, string> ChallengeIngressAnnotations => GetDictionary("ChallengeIngress:Annotations");

    public bool UseChallengeIngressLabels => GetBool("ChallengeIngress:UseLabels");
    public Dictionary<string, string> ChallengeIngressLabels => GetDictionary("ChallengeIngress:Labels");

    public TimeSpan ChallengeIngressMaxPropagationWaitTime => TimeSpan.FromSeconds(GetInt("ChallengeIngress:MaxPropagationWaitTimeSeconds"));
    public TimeSpan ChallengeIngressPropagationCheckInterval => TimeSpan.FromMilliseconds(GetInt("ChallengeIngress:PropagationCheckIntervalMilliseconds"));
    
    public TimeSpan AcmeWaitTime => TimeSpan.FromSeconds(_cfg.GetValue<int>("Acme:ValidationWaitTimeSeconds"));
    public int AcmeNumRetries => _cfg.GetValue<int>("Acme:ValidationNumRetries");
    public bool EnableAutoRenew => GetBool("Acme:AutoRenewal");
    public TimeSpan RenewalTimeBetweenChecks => TimeSpan.FromHours(_cfg.GetValue<int>("Acme:RenewalCheckTimeHours"));
    public TimeSpan RenewalExpirationLimit => TimeSpan.FromDays(_cfg.GetValue<int>("Acme:RenewalThresholdDays"));

    public Uri AcmeDir => new(GetString("Acme:DirUrl"));
    public string AcmeEmail => GetString("Acme:Email");
    public string AcmeKey => GetString("Acme:Key") ?? _key; // If no key is provided via configs, use generated key.
    public bool AcmeAccepted => GetBool("Acme:TermsAccepted");

    public string AcmeEabKeyId => GetString("Acme:EabKeyId");
    public string AcmeHmacKey => GetString("Acme:EabHmacKey");

    public string SmtpEmailFrom => GetString("Smtp:EmailFrom");
    public string SmtpHost => GetString("Smtp:Host");
    public int SmtpPort => GetInt("Smtp:Port");
    public string SmtpUser => GetString("Smtp:User");
    public string SmtpPass => GetString("Smtp:Pass");

    public string IngressLabelValue => GetString("ChallengeIngress:IngressLabelValue");

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
            NamespaceConstraintsList,
            HttpTimeout,
            EnableHttpRetry
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
    };

    private static string HideString(string val) => string.IsNullOrEmpty(val) ? null : "[REDACTED]";
    private string GetString(string key) => _cfg.GetValue<string>(key);
    private int GetInt(string key) => _cfg.GetValue<int>(key);
    private int? GetOptionalInt(string key) => _cfg.GetValue<int?>(key);
    private bool GetBool(string key) => _cfg.GetValue<bool>(key);
    private bool? GetOptionalBool(string key) => _cfg.GetValue<bool?>(key);

    private Dictionary<string, string> GetDictionary(string key)
    {
        var data = _cfg.GetSection(key)?.GetChildren() ?? Enumerable.Empty<IConfigurationSection>();
        return data.ToDictionary(s => s.Key, s => s.Value);
    }
    
}