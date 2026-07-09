namespace KCert.Models;

public class RenewalException(string message, Exception inner) : Exception(message, inner)
{
    public string SecretNamespace { get; set; } = default!;
    public string SecretName { get; set; } = default!;
    public List<string> Logs { get; set; } = default!;
}
