namespace KCert.Models;

public class AcmeOrderResponse : AcmeResponse
{
    public string Status { get; set; } = default!;
    public string Expires { get; set; } = default!;
    public AcmeIdentifier[] Identifiers { get; set; } = default!;
    public string[] Authorizations { get; set; } = default!;
    public string Finalize { get; set; } = default!;
    public string Certificate { get; set; } = default!;
}
