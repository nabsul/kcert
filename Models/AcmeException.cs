namespace KCert.Models;

public class AcmeException : Exception
{
    public required AcmeError Error { get; init; }
}