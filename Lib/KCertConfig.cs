using Microsoft.Extensions.Configuration;
using System;

namespace KCert.Lib
{
    [Service]
    public class KCertConfig
    {
        private readonly IConfiguration _cfg;

        public KCertConfig(IConfiguration cfg)
        {
            _cfg = cfg;
        }

        public string K8sConfigFile => _cfg["Config"];
        public string KCertNamespace => _cfg.GetValue<string>("Namespace");
        public string Label => _cfg.GetValue<string>("Label");
        public string KCertSecretName => _cfg.GetValue<string>("SecretName");
        public string KCertServiceName => _cfg.GetValue<string>("ServiceName");
        public string KCertIngressName => _cfg.GetValue<string>("IngressName");
        public string KCertServicePort => _cfg.GetValue<string>("ServicePort");

        public TimeSpan AcmeWaitTime => TimeSpan.FromSeconds(_cfg.GetValue<int>("AcmeWaitTimeSeconds"));
        public int AcmeNumRetries => _cfg.GetValue<int>("AcmeNumRetries");

        public TimeSpan RenewalTimeBetweenChekcs => TimeSpan.FromHours(_cfg.GetValue<int>("RenewalCheckTimeHours"));
        public TimeSpan RenewalExpirationLimit => TimeSpan.FromDays(_cfg.GetValue<int>("RenewalExpirationRenewalDays"));
        public TimeSpan IngressUpdateWaitTime => TimeSpan.FromSeconds(_cfg.GetValue<int>("IngressUpdateWaitSeconds"));
    }
}
