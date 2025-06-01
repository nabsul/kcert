namespace KCert.Models;

public class AcmeError
{
    public required string Type { get; init; }
    public required string Detail { get; init; }
    public int Status { get; init; }
}