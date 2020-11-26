using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace KCert.Lib.Acme
{
    public class AcmeResponse
    {
        public string Nonce { get; set; }
        public string Location { get; set; }
        public JsonDocument Content { get; set; }

        // Helper functions to extract commonly used information
        public List<Uri> AuthorizationUrls => Content.RootElement.GetProperty("authorizations").EnumerateArray().Select(i => new Uri(i.GetString())).ToList();
        public Uri FinalizeUri => new Uri(Content.RootElement.GetProperty("finalize").GetString());

        public Uri HttpChallengeUri => new Uri(HttpChallenge.GetProperty("url").GetString());

        public bool IsChallengeDone => Content.RootElement.GetProperty("challenges").EnumerateArray()
                .Select(c => c.GetProperty("status").GetString()).Any(s => s == "valid");

        public Uri CertUri => new Uri(Content.RootElement.GetProperty("certificate").GetString());

        public bool IsOrderFinalized => Content.RootElement.GetProperty("status").GetString() == "valid";

        private JsonElement HttpChallenge => Content.RootElement.GetProperty("challenges").EnumerateArray()
            .FirstOrDefault(c => c.GetProperty("type").GetString() == "http-01");
    }
}
