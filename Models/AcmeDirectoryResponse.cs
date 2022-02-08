namespace KCert.Models;

public class AcmeDirectoryResponse
{
    public string KeyChange { get; set; }
    public string NewAccount { get; set; }
    public string NewNonce { get; set; }
    public string NewOrder { get; set; }
    public string RevokeCert { get; set; }

    public DirectoryMeta Meta { get; set; }

    public class DirectoryMeta
    {
        public string[] CaaIdentities { get; set; }
        public string TermsOfService { get; set; }
        public string Website { get; set; }
    }
}
