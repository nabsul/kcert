using System.Text.Json.Serialization;

namespace KCert.Models;

public class AcmeResponse
{
    [JsonIgnore]
    public string Location { get; set; } = default!;
}
