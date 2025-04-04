using System.Text.Json.Serialization;

namespace KCert.Models;

public class AcmeOrderResponse : AcmeResponse, IHasLocationHeader
{
    public string Status { get; set; } = default!;
    public string Expires { get; set; } = default!;
    public AcmeIdentifier[] Identifiers { get; set; } = default!;
    public string[] Authorizations { get; set; } = default!;
    public string Finalize { get; set; } = default!;
    public string Certificate { get; set; } = default!;

    [JsonIgnore] 
    public string Location { get; set; } = default!;
}

