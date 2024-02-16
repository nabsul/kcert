namespace KCert.Models;

public class AcmeDirectoryResponse
{
    public string KeyChange { get; set; } = default!;
    public string NewAccount { get; set; } = default!;
    public string NewNonce { get; set; } = default!;
    public string NewOrder { get; set; } = default!;
    public string RevokeCert { get; set; } = default!;

    public DirectoryMeta Meta { get; set; } = default!;

    public class DirectoryMeta
    {
        public string[] CaaIdentities { get; set; } = default!;
        public string TermsOfService { get; set; } = default!;
        public string Website { get; set; } = default!;
    }
}
