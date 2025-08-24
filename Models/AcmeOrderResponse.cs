using System.Text.Json.Nodes;

namespace KCert.Models;

public class AcmeOrderResponse : AcmeResponse
{
    public required string Status { get; init; }
    public required string Expires { get; init; }
    public AcmeIdentifier[] Identifiers { get; init; }
    public required string[] Authorizations { get; init; }
    public required string Finalize { get; init; }
    public required string Certificate { get; init; }
    
    public JsonObject? Error { get; init; }
}
