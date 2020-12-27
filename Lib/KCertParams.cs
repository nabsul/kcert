using k8s.Models;
using KCert.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KCert.Lib
{
    public class KCertParams
    {
        public Uri AcmeDirUrl { get => GetUri(nameof(AcmeDirUrl)); set => SetValue(nameof(AcmeDirUrl), value); }
        public bool TermsAccepted { get => GetBool(nameof(TermsAccepted)); set => SetValue(nameof(TermsAccepted), value); }
        public string AcmeEmail { get => GetString(nameof(AcmeEmail)); set => SetValue(nameof(AcmeEmail), value); }
        public string AcmeKey { get => GetString(nameof(AcmeKey)); set => SetValue(nameof(AcmeKey), value); }

        public bool EnableAutoRenew { get => GetBool(nameof(EnableAutoRenew)); set => SetValue(nameof(EnableAutoRenew), value); }
        public string AwsKey { get => GetString(nameof(AwsKey)); set => SetValue(nameof(AwsKey), value); }
        public string AwsRegion { get => GetString(nameof(AwsRegion)); set => SetValue(nameof(AwsRegion), value); }
        public string AwsSecret { get => GetString(nameof(AwsSecret)); set => SetValue(nameof(AwsSecret), value); }
        public string EmailFrom { get => GetString(nameof(EmailFrom)); set => SetValue(nameof(EmailFrom), value); }

        private readonly Dictionary<string, byte[]> _data;

        public KCertParams()
        {
            _data = new Dictionary<string, byte[]>();
        }

        public KCertParams(V1Secret secret)
        {
            _data = secret.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        private bool GetBool(string key) => bool.Parse(GetString(key));

        private Uri GetUri(string key)
        {
            var uri = GetString(key);
            return uri == null ? null : new Uri(uri);
        }

        public IDictionary<string, byte[]> Export() => _data;
        private string GetString(string k)
        {
            if (!_data.TryGetValue(k, out var b))
            {
                return null;
            }

            return Encoding.UTF8.GetString(b);
        }

        private void SetValue(string k, bool v) => SetValue(k, v.ToString());

        private void SetValue(string k, Uri uri) => SetValue(k, uri.AbsoluteUri);

        private void SetValue(string k, string value)
        {
            _data[k] = Encoding.UTF8.GetBytes(value);
        }
    }
}
