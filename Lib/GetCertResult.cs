using System;
using System.Collections.Generic;

namespace KCert.Lib
{
    public class GetCertResult
    {
        public string IngressName { get; set; }
        public bool Success { get; set; }
        public List<string> Logs { get; set; } = new List<string>();
        public Exception Error { get; set; }
    }
}
