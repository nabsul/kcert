using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KCert.Lib
{
    [Service]
    public class KCertConfig
    {
        private IConfiguration _cfg;

        public KCertConfig(IConfiguration cfg)
        {
            _cfg = cfg;
        }

        public string KCertNamespace => _cfg.GetValue<string>("KCertNamespace");
        public string KCertSecretName => _cfg.GetValue<string>("SecretName");
        public string KCertServiceName => _cfg.GetValue<string>("ServiceName");
        public string KCertIngressName => _cfg.GetValue<string>("IngressName");
        public string KCertServicePort => _cfg.GetValue<string>("ServicePort");
        public List<string> ManagedNamespaces => _cfg.GetValue<string>("Namespaces").Split(',').ToList();

        public TimeSpan AcmeWaitTime => TimeSpan.FromSeconds(_cfg.GetValue<int>("AcmeWaitTimeSeconds"));
        public int AcmeNumRetries => _cfg.GetValue<int>("AcmeNumRetries");

        public TimeSpan RenewalTimeBetweenChekcs => TimeSpan.FromHours(_cfg.GetValue<int>("RenewalCheckTimeHours"));
        public TimeSpan RenewalExpirationLimit => TimeSpan.FromDays(_cfg.GetValue<int>("RenewalExpirationRenewalDays"));
        public TimeSpan IngressUpdateWaitTime => TimeSpan.FromSeconds(_cfg.GetValue<int>("IngressUpdateWaitSeconds"));
    }
}
