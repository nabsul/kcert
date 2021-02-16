namespace KCert.Models
{
    public class AcmeAuthzResponse : AcmeResponse
    {
        public string Status { get; set; }
        public string Expires { get; set; }
        public AcmeIdentifier Identifier { get; set; }
        public AcmeChallenge[] Challenges { get; set; }
        public bool Wildcard { get; set; }
    }
}
