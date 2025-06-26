using System.Security.Cryptography;
using System.Text;
using KCert.Models;
using KCert.Services;
using Microsoft.AspNetCore.Authentication;

namespace KCert.Challenge;

[Service]
public class DnsUtils(CertClient cert)
{
    private static string StripWildcard(string domain) => domain.StartsWith("*.") ? domain[2..] : domain;

    public (string Key, string Value) GetDnsParameters(AcmeAuthzResponse auth, string domain)
    {
        string originalIdentifier = auth.Identifier.Value;
        string domainForDnsChallenge = StripWildcard(domain);
        var dnsChallenge = auth.Challenges.First(c => c.Type == "dns-01");
        string txtRecordName = $"_acme-challenge.{domainForDnsChallenge}";
        string keyAuth = cert.GetKeyAuthorization(dnsChallenge.Token);
        using var sha256 = SHA256.Create();
        var txtRecordValue = Base64UrlTextEncoder.Encode(sha256.ComputeHash(Encoding.UTF8.GetBytes(keyAuth)));
        return (txtRecordName, txtRecordValue);
    }
}