using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace KCert.Lib
{
    public class KCertConfig
    {
        private IConfiguration _cfg;

        public KCertConfig(IConfiguration cfg)
        {
            _cfg = cfg;
        }

        public string KCertNamespace => _cfg.GetValue<string>("Deploy:Namespace");
        public string KCertSecretName => _cfg.GetValue<string>("Deploy:SecretName");
        public string KCertServiceName => _cfg.GetValue<string>("Deploy:ServiceName");
        public string KCertServicePort => _cfg.GetValue<string>("Deploy:ServicePort");
        public List<string> ManagedNamespaces => _cfg.GetValue<List<string>>("Deploy:ManagedNamespaces");

        public TimeSpan AcmeWaitTime => TimeSpan.FromSeconds(_cfg.GetValue<int>("ACME:AcmeWaitTimeSeconds"));
        public int AcmeNumRetries => _cfg.GetValue<int>("ACME:AcmeNumRetries");
        public TimeSpan RenewalTimeBetweenChekcs => TimeSpan.FromHours(_cfg.GetValue<int>("ACME:RenewalCheckTimeHours"));
        public TimeSpan RenewalExpirationLimit => TimeSpan.FromDays(_cfg.GetValue<int>("ACME:RenewalExpirationRenewalDays"));
    }
}
