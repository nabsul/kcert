using System;

namespace KCert.Models
{
    public class HomeViewModel
    {
        public string Namespace { get; set; }
        public string IngressName { get; set; }
        public string SecretName { get; set; }
        public string[] Hosts { get; set; }
        public DateTime? Created { get; set; }
        public DateTime? Expires { get; set; }
        public bool HasChallengeEntry { get; set; }
    }
}
