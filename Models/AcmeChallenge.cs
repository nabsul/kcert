using System.Text.Json.Nodes;

namespace KCert.Models;

public class AcmeChallenge
{
    public required string Url { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public required string Token { get; init; }
    public required string Validated { get; init; } 
    
    public JsonObject? Error { get; init; }
}
