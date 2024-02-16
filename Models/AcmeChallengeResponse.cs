namespace KCert.Models;

public class AcmeChallengeResponse : AcmeResponse
{
    public string Type { get; set; } = default!;
    public string Url { get; set; } = default!;
    public string Status { get; set; } = default!;
    public string Token { get; set; } = default!;
}
