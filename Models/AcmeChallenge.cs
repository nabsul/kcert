namespace KCert.Models;

public class AcmeChallenge
{
    public string Url { get; set; }
    public string Type { get; set; }
    public string Status { get; set; }
    public string Token { get; set; }
    public string Validated { get; set; }
}
