namespace KCert.Models;

public class AcmeChallenge
{
    public string Url { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string Token { get; set; } = default!;
    public string Validated { get; set; } = default!;
}
