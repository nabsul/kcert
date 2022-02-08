using System;
using System.Collections.Generic;

namespace KCert.Models;

public class RenewalException : Exception
{
    public string SecretNamespace { get; set; }
    public string SecretName { get; set; }
    public List<string> Logs { get; set; }

    public RenewalException(string message, Exception inner) : base(message, inner) { }
}
