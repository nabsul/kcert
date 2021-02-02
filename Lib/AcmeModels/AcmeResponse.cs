using System.Text.Json.Serialization;

namespace KCert.Lib.AcmeModels
{
    public class AcmeResponse
    {
        [JsonIgnore]
        public string Nonce { get; set; }

        [JsonIgnore]
        public string Location { get; set; }
    }
}
