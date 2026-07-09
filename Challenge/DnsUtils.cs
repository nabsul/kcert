namespace KCert.Challenge;

using System.Security.Cryptography;
using System.Text;
using KCert.Models;
using KCert.Services;
using Microsoft.AspNetCore.Authentication;

public class DnsUtils(CertClient cert)
{
    public string StripWildcard(string domain) => domain.StartsWith("*.") ? domain[2..] : domain;

    public string GetTextRecordKey(string domain) => $"_acme-challenge.{domain}";

    public string GetTextRecordValue(AcmeAuthzResponse auth, string domain)
    {
        var dnsChallenge = auth.Challenges.First(c => c.Type == "dns-01");
        var keyAuth = Encoding.UTF8.GetBytes($"{dnsChallenge.Token}.{cert.GetThumbprint()}");
        var txtRecordValue = Base64UrlTextEncoder.Encode(SHA256.HashData(keyAuth));
        return txtRecordValue;
    }
}
