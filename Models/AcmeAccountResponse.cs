namespace KCert.Models;

public class AcmeAccountResponse : AcmeResponse
{
    public string Status { get; set; }
    public string[] Contact { get; set; }
    public string Orders { get; set; }
}
