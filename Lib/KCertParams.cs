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
        public string AcmeKey { get; set; }

        public bool EnableAutoRenew { get; set; }
        public string SendGridKey { get; set; }
        public string SendGridFrom { get; set; }

        public KCertParams()
        {
        }

        public KCertParams(V1Secret secret)
        {
            var data = secret.Data;
            AcmeDirUrl = new Uri(GetString(data, "AcmeDirUrl"));
            TermsAccepted = bool.Parse(GetString(data, "TermsAccepted") ?? "false");
            Email = GetString(data, "AcmeEmail");
            AcmeKey = GetString(data, "AcmeKey");

            EnableAutoRenew = bool.Parse(GetString(data, "EnableAutoRenew") ?? "false");
            SendGridKey = GetString(data, "SendGridKey");
            SendGridFrom = GetString(data, "SendGridFrom");
        }

        public IDictionary<string, byte[]> Export()
        {
            return new Dictionary<string, byte[]>
            {
                { "AcmeDirUrl", Encoding.UTF8.GetBytes(AcmeDirUrl.AbsoluteUri) },
                { "TermsAccepted", Encoding.UTF8.GetBytes(TermsAccepted.ToString()) },
                { "AcmeEmail", Encoding.UTF8.GetBytes(Email) },
                { "AcmeKey", Encoding.UTF8.GetBytes(AcmeKey) },

                { "EnableAutoRenew", Encoding.UTF8.GetBytes(EnableAutoRenew.ToString()) },
                { "SendGridKey", Encoding.UTF8.GetBytes(SendGridKey) },
                { "SendGridFrom", Encoding.UTF8.GetBytes(SendGridFrom) },
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
