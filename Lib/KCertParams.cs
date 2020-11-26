using k8s.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace KCert.Lib
{
    public class KCertParams
    {
        public Uri AcmeDirUrl { get; set; }
        public bool TermsAccepted { get; set; }
        public string Email { get; set; }
        public string Key { get; set; }

        public KCertParams()
        {
        }

        public KCertParams(V1Secret secret)
        {
            var data = secret.Data;
            AcmeDirUrl = new Uri(GetString(data, "AcmeDirUrl"));
            TermsAccepted = bool.Parse(GetString(data, "TermsAccepted"));
            Email = GetString(data, "AcmeEmail");
            Key = GetString(data, "AcmeKey");
        }

        public IDictionary<string, byte[]> Export()
        {
            return new Dictionary<string, byte[]>
            {
                { "AcmeDirUrl", Encoding.UTF8.GetBytes(AcmeDirUrl.AbsoluteUri) },
                { "TermsAccepted", Encoding.UTF8.GetBytes(TermsAccepted.ToString()) },
                { "AcmeEmail", Encoding.UTF8.GetBytes(Email) },
                { "AcmeKey", Encoding.UTF8.GetBytes(Key) },
            };
        }

        private static string GetString(IDictionary<string, byte[]> data, string k)
        {
            if (!data.TryGetValue(k, out var b))
            {
                return null;
            }

            return Encoding.UTF8.GetString(b);
        }
    }
}
