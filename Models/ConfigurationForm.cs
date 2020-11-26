namespace KCert.Models
{
    public class ConfigurationForm
    {
        public string AcmeDir { get; set; }
        public string AcmeEmail { get; set; }
        public bool TermsAccepted { get; set; }
        public bool NewKey { get; set; }
    }
}
