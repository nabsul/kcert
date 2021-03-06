﻿using k8s.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace KCert.Models
{
    public class KCertParams
    {
        public Uri AcmeDirUrl { get; set; }
        public bool TermsAccepted { get; set; }
        public string AcmeEmail { get; set; }
        public string AcmeKey { get; set; }

        public bool EnableAutoRenew { get; set; }
        public string EmailFrom { get; set; }
        public string SmtpHost { get; set; }
        public int SmtpPort { get; set; }
        public string SmtpUser { get; set; }
        public string SmtpPass { get; set; }

        public KCertParams() { }

        public KCertParams(V1Secret secret)
        {
            var data = secret.Data.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            foreach (var p in typeof(KCertParams).GetProperties())
            {
                if (!data.TryGetValue(p.Name, out var value))
                {
                    continue;
                }

                object result = null;
                if (value != null)
                {
                    if (!GetValLookup.TryGetValue(p.PropertyType, out var func))
                    {
                        throw new Exception($"Don't know how to get Property {p.Name} of type {p.PropertyType}");
                    }

                    result = func(Encoding.UTF8.GetString(value));
                }

                p.SetValue(this, result);
            }
        }

        public IDictionary<string, byte[]> Export()
        {
            var result = new Dictionary<string, byte[]>();
            foreach (var p in typeof(KCertParams).GetProperties())
            {
                byte[] value = null;
                var propValue = p.GetValue(this);
                if (propValue != null)
                {
                    if (!SetValLookup.TryGetValue(p.PropertyType, out var func))
                    {
                        throw new Exception($"Don't know how to set Property {p.Name} of type {p.PropertyType}");
                    }

                    value = Encoding.UTF8.GetBytes(func(propValue));
                }

                result.Add(p.Name, value);
            }

            return result;
        }

        private static readonly Dictionary<Type, Func<string, object>> GetValLookup = new Dictionary<Type, Func<string, object>>
        {
            { typeof(string), (str) => str },
            { typeof(bool), (str) => bool.Parse(str) },
            { typeof(Uri), (str) => new Uri(str) },
            { typeof(int), (str) => int.Parse(str) },
        };

        private static readonly Dictionary<Type, Func<object, string>> SetValLookup = new Dictionary<Type, Func<object, string>>
        {
            { typeof(string), (obj) => (string)obj },
            { typeof(bool), (obj) => ((bool)obj).ToString() },
            { typeof(Uri), (obj) => ((Uri)obj).AbsoluteUri },
            { typeof(int), (obj) => obj.ToString() },
        };
    }
}
