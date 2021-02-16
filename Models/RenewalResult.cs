using System;
using System.Collections.Generic;

namespace KCert.Models
{
    public class RenewalResult
    {
        public string SecretNamespace { get; set; }
        public string SecretName { get; set; }
        public bool Success { get; set; }
        public List<string> Logs { get; set; } = new List<string>();
        public Exception Error { get; set; }
    }
}
