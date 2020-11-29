namespace KCert.Models
{
    public class ConfigurationForm
    {
        public string AcmeDir { get; set; }
        public string AcmeEmail { get; set; }
        public bool TermsAccepted { get; set; }
        public bool NewKey { get; set; }
        
        public bool EnableAutoRenew { get; set; }
        public string AwsRegion { get; set; }
        public string AwsKey { get; set; }
        public string AwsSecret { get; set; }
        public string EmailFrom { get; set; }
    }
}
