namespace KCert.Models;

public class AcmeAccountResponse : AcmeResponse
{
    public string Status { get; set; } = default!;
    public string[] Contact { get; set; } = default!;
    public string Orders { get; set; } = default!;
}
