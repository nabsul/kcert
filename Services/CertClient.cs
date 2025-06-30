﻿using k8s.Models;
using Microsoft.AspNetCore.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace KCert.Services;

public class CertClient(KCertConfig cfg)
{
    private const int PEMLineLen = 64;
    private const string PEMStart = "-----BEGIN PRIVATE KEY-----";
    private const string PEMEnd = "-----END PRIVATE KEY-----";
    private const string SanOid = "2.5.29.17";

    private readonly RSA _rsa = RSA.Create(2048);

    public X509Certificate2 GetCert(V1Secret secret)
    {
        var certData = secret.Data["tls.crt"] ?? throw new ArgumentException("Certificate data is missing in the secret.");
        return X509Certificate2.CreateFromPem(Encoding.UTF8.GetString(certData));
    }

    public List<string> GetHosts(X509Certificate2 cert)
    {
        var simpleName = cert.GetNameInfo(X509NameType.SimpleName, false);

        var sans = cert.Extensions.Cast<X509Extension>()
            .Where(e => e.Oid?.Value == SanOid)
            .SelectMany(ext => ext.Format(false).Split(','))
            .Select(s => s.Trim().Split(':', '=').Skip(1).FirstOrDefault())
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Cast<string>();

        return new[] { simpleName }.Concat(sans).Distinct().ToList();
    }

    public object GetJwk(ECDsa sign)
    {
        var p = sign.ExportParameters(false);
        return new
        {
            crv = "P-256",
            kty = "EC",
            x = Base64UrlTextEncoder.Encode(p.Q.X ?? throw new Exception("ECC X parameter not defined")),
            y = Base64UrlTextEncoder.Encode(p.Q.Y ?? throw new Exception("ECC Y parameter not defined")),
        };
    }

    public static string GenerateNewKey()
    {
        var sign = ECDsa.Create();
        sign.KeySize = 256;
        var key = sign.ExportECPrivateKey();
        return Base64UrlTextEncoder.Encode(key);
    }

    public string GetThumbprint()
    {
        var sign = GetSigner(cfg.AcmeKey);
        var jwk = GetJwk(sign);
        var jwkJson = JsonSerializer.Serialize(jwk);
        var jwkBytes = Encoding.UTF8.GetBytes(jwkJson);
        var result = SHA256.HashData(jwkBytes);
        return Base64UrlTextEncoder.Encode(result);
    }

    public ECDsa GetSigner(string key)
    {
        var sign = ECDsa.Create();
        sign.KeySize = 256;
        sign.ImportECPrivateKey(Base64UrlTextEncoder.Decode(key), out _);
        return sign;
    }

    public byte[] SignData(string key, byte[] data)
    {
        var sign = GetSigner(key);
        return sign.SignData(data, HashAlgorithmName.SHA256);
    }

    public string GetCsr(IEnumerable<string> hosts)
    {
        var domain = hosts.First();
        var req = new CertificateRequest($"CN={domain}", _rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        hosts.ToList().ForEach(sanBuilder.AddDnsName);
        req.CertificateExtensions.Add(sanBuilder.Build());

        var signed = req.CreateSigningRequest();
        return Base64UrlTextEncoder.Encode(signed);
    }

    public string GetPemKey()
    {
        var rsa = _rsa;
        var key = rsa.ExportPkcs8PrivateKey();
        var str = Convert.ToBase64String(key);
        return string.Join('\n', PEMStart, InsertNewLines(str), PEMEnd);
    }

    private static string InsertNewLines(string input) => string.Join('\n', SplitLines(input));

    private static IEnumerable<string> SplitLines(string input)
    {
        int start = 0;
        while (start < input.Length)
        {
            var len = Math.Min(input.Length - start, PEMLineLen);
            yield return input.Substring(start, len);
            start += PEMLineLen;
        }
    }
}
