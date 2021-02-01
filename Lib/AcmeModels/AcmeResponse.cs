namespace KCert.Lib.AcmeModels
{
    public class AcmeResponse<T>
    {
        public string Nonce { get; set; }
        public string Location { get; set; }
        public string Content { get; set; }
        public T Response { get; set; }
    }
}
