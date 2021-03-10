namespace KCert.Models
{
    public class ConfigurationForm
    {
        public string AcmeDir { get; set; }
        public string AcmeEmail { get; set; }
        public string AcmeKey { get; set; }
        public bool TermsAccepted { get; set; }
        
        public bool EnableAutoRenew { get; set; }
        public string EmailFrom { get; set; }
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; }
        public string SmtpUser { get; set; }
        public string SmtpPass { get; set; }
    }
}
