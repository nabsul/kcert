using System.Text.Json.Serialization;

namespace KCert.Models;

public class AcmeAccountResponse : AcmeResponse, IHasLocationHeader
{
    public string Status { get; set; } = default!;
    public string[] Contact { get; set; } = default!;
    public string Orders { get; set; } = default!;
    
    [JsonIgnore] 
    public string Location { get; set; } = default!;
}
