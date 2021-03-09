using k8s.Models;
using KCert.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KCert.Models
{
    public class HomeViewModel
    {
        public string Namespace { get; set; }
        public string SecretName { get; set; }
        public string[] Hosts { get; set; }
        public DateTime? Created { get; set; }
        public DateTime? Expires { get; set; }
        public bool HasChallengeEntry { get; set; }

        public HomeViewModel(V1Secret s, HashSet<string> configuredHosts, CertClient certClient)
        {
            var cert = certClient.GetCert(s);
            var hosts = new string[] { cert.Subject[3..] };

            Namespace = s.Namespace();
            SecretName = s.Name();
            Hosts = hosts;
            HasChallengeEntry = hosts.All(configuredHosts.Contains);
            Created = cert.NotBefore;
            Expires = cert.NotAfter;
        }
    }
}
