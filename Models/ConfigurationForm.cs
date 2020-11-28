namespace KCert.Models
{
    public class ConfigurationForm
    {
        public string AcmeDir { get; set; }
        public string AcmeEmail { get; set; }
        public bool TermsAccepted { get; set; }
        public bool NewKey { get; set; }
        
        public bool EnableAutoRenew { get; set; }
        public string SendGridKey { get; set; }
        public string SendGridFrom { get; set; }
    }
}
