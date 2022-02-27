﻿using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
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

    public bool WatchIngresses => GetBool("KCert:WatchIngresses");
    public string K8sConfigFile => _cfg["Config"];
    public bool AcceptAllChallenges => GetBool("KCert:AcceptAllChallenges");
    public string KCertNamespace => GetString("KCert:Namespace");
    public string KCertSecretName => GetString("KCert:SecretName");
    public string KCertServiceName => GetString("KCert:ServiceName");
    public string KCertIngressName => GetString("KCert:IngressName");
    public int KCertServicePort => GetInt("KCert:ServicePort");

    public Dictionary<string, string> ChallengeIngressAnnotations => GetDictionary("ChallengeIngress:Annotations");

    public Dictionary<string, string> ChallengeIngressLabels => GetDictionary("ChallengeIngress:Labels");

    public TimeSpan AcmeWaitTime => TimeSpan.FromSeconds(_cfg.GetValue<int>("Acme:ValidationWaitTimeSeconds"));
    public int AcmeNumRetries => _cfg.GetValue<int>("Acme:ValidationNumRetries");
    public bool EnableAutoRenew => GetBool("Acme:AutoRenewal");
    public TimeSpan RenewalTimeBetweenChecks => TimeSpan.FromHours(_cfg.GetValue<int>("Acme:RenewalCheckTimeHours"));
    public TimeSpan RenewalExpirationLimit => TimeSpan.FromDays(_cfg.GetValue<int>("Acme:RenewalExpirationRenewalDays"));

    public Uri AcmeDir => new(GetString("Acme:DirUrl"));
    public string AcmeEmail => GetString("Acme:Email");
    public string AcmeKey => GetString("Acme:Key");
    public bool AcmeAccepted => GetBool("Acme:TermsAccepted");

    public string SmtpEmailFrom => GetString("Smtp:EmailFrom");
    public string SmtpHost => GetString("Smtp:Host");
    public int SmtpPort => GetInt("Smtp:Port");
    public string SmtpUser => GetString("Smtp:User");
    public string SmtpPass => GetString("Smtp:Pass");

    public object AllConfigs => new
    {
        KCert = new
        {
            Namespace = KCertNamespace,
            IngressName = KCertIngressName,
            SecertName = KCertSecretName,
            ServiceName = KCertServiceName,
            ServicePort = KCertServicePort
        },
        ACME = new
        {
            ValidationWaitTimeSeconds = AcmeWaitTime,
            ValidationNumRetries = AcmeNumRetries,
            AutoRenewal = EnableAutoRenew,
            RenewalCheckTimeHours = RenewalTimeBetweenChecks,
            RenewalExpirationRenewalDays = RenewalExpirationLimit,
            TermsAccepted = AcmeAccepted,
            DirUrl = AcmeDir,
            Email = AcmeEmail,
            Key = HideString(AcmeKey)
        },
        SMTP = new
        {
            EmailFrom = SmtpEmailFrom,
            Host = SmtpHost,
            Port = SmtpPort,
            User = SmtpUser,
            Pass = HideString(SmtpPass)
        },
    };

    private static string HideString(string val) => string.IsNullOrEmpty(val) ? null : "[REDACTED]";
    private string GetString(string key) => _cfg.GetValue<string>(key);
    private int GetInt(string key) => _cfg.GetValue<int>(key);
    private bool GetBool(string key) => _cfg.GetValue<bool>(key);

    private Dictionary<string, string> GetDictionary(string key)
    {
        var data = _cfg.GetSection(key)?.GetChildren() ?? Enumerable.Empty<IConfigurationSection>();
        return data.ToDictionary(s => s.Key, s => s.Value);
    }
}
