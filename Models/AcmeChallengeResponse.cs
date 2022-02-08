namespace KCert.Models;

public class AcmeChallengeResponse : AcmeResponse
{
    public string Type { get; set; }
    public string Url { get; set; }
    public string Status { get; set; }
    public string Token { get; set; }
}
