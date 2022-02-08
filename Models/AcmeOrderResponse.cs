namespace KCert.Models;

public class AcmeOrderResponse : AcmeResponse
{
    public string Status { get; set; }
    public string Expires { get; set; }
    public AcmeIdentifier[] Identifiers { get; set; }
    public string[] Authorizations { get; set; }
    public string Finalize { get; set; }
    public string Certificate { get; set; }
}
