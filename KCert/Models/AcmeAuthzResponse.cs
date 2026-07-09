namespace KCert.Models;

public class AcmeAuthzResponse : AcmeResponse
{
    public string Status { get; set; } = default!;
    public string Expires { get; set; } = default!;
    public AcmeIdentifier Identifier { get; set; } = default!;
    public AcmeChallenge[] Challenges { get; set; } = default!;
    public bool Wildcard { get; set; } = default!;
}
