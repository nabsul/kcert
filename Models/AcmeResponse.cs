using System.Text.Json.Serialization;

namespace KCert.Models;

public class AcmeResponse
{
    [JsonIgnore]
    public string Nonce { get; set; } = default!;

}
