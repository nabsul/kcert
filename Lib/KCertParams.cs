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
        public string AcmeEmail { get; set; }
        public string AcmeKey { get; set; }

        public bool EnableAutoRenew { get; set; }
        public string AwsKey { get; set; }
        public string AwsRegion { get; set; }
        public string AwsSecret { get; set; }
        public string EmailFrom { get; set; }

        public KCertParams()
        {
        }

        public KCertParams(V1Secret secret)
        {
            var data = secret.Data;
            AcmeDirUrl = new Uri(GetString(data, "AcmeDirUrl"));
            TermsAccepted = bool.Parse(GetString(data, "TermsAccepted") ?? "false");
            AcmeEmail = GetString(data, "AcmeEmail");
            AcmeKey = GetString(data, "AcmeKey");

            EnableAutoRenew = bool.Parse(GetString(data, "EnableAutoRenew") ?? "false");
            AwsRegion = GetString(data, "AwsRegion");
            AwsKey = GetString(data, "AwsKey");
            AwsSecret = GetString(data, "AwsSecret");
            EmailFrom = GetString(data, "EmailFrom");
        }

        public IDictionary<string, byte[]> Export()
        {
            return new Dictionary<string, byte[]>
            {
                { "AcmeDirUrl", Encoding.UTF8.GetBytes(AcmeDirUrl.AbsoluteUri) },
                { "TermsAccepted", Encoding.UTF8.GetBytes(TermsAccepted.ToString()) },
                { "AcmeEmail", Encoding.UTF8.GetBytes(AcmeEmail) },
                { "AcmeKey", Encoding.UTF8.GetBytes(AcmeKey) },

                { "EnableAutoRenew", Encoding.UTF8.GetBytes(EnableAutoRenew.ToString()) },
                { "AwsRegion", Encoding.UTF8.GetBytes(AwsRegion) },
                { "AwsKey", Encoding.UTF8.GetBytes(AwsKey) },
                { "AwsSecret", Encoding.UTF8.GetBytes(AwsSecret) },
                { "EmailFrom", Encoding.UTF8.GetBytes(EmailFrom) },
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
