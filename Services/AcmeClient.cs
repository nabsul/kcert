using KCert.Models;
using KCert.Tools;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class AcmeClient
{
    private const string HeaderReplayNonce = "Replay-Nonce";
    private const string HeaderLocation = "Location";
    private const string ContentType = "application/jose+json";
    private const string Alg = "ES256";

    private static readonly JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http = new();
    private readonly CertClient _cert;
    private readonly KCertConfig _cfg;
    private readonly BufferedLogger<RenewalHandler> _log;

    private readonly Base64Tool _b64 = new Base64Tool();

    public AcmeClient(CertClient cert, BufferedLogger<RenewalHandler> log, KCertConfig cfg)
    {
        _cert = cert;
        _log = log;
        _cfg = cfg;

        _http.Timeout = _cfg.HttpTimeout;
    }

    private AcmeDirectoryResponse _dir;

    public Task<AcmeAccountResponse> DeactivateAccountAsync(string key, string kid, string nonce) => PostAsync<AcmeAccountResponse>(key, new Uri(kid), new { status = "deactivated" }, kid, nonce);
    public Task<AcmeOrderResponse> GetOrderAsync(string key, Uri uri, string kid, string nonce) => PostAsync<AcmeOrderResponse>(key, uri, null, kid, nonce);
    public Task<AcmeAuthzResponse> GetAuthzAsync(string key, Uri authzUri, string kid, string nonce) => PostAsync<AcmeAuthzResponse>(key, authzUri, null, kid, nonce);
    public Task<AcmeChallengeResponse> TriggerChallengeAsync(string key, Uri challengeUri, string kid, string nonce) => PostAsync<AcmeChallengeResponse>(key, challengeUri, new { }, kid, nonce);

    public async Task ReadDirectoryAsync(Uri dirUri)
    {
        using var resp = await _http.GetAsync(dirUri);
        if (!resp.IsSuccessStatusCode)
        {
            var result = await resp.Content.ReadAsStringAsync();
            var message = $"Failed to read ACME directory with error response code {resp.StatusCode} and message: {result}";
            throw new Exception(message);
        }

        using var stream = await resp.Content.ReadAsStreamAsync();
        _dir = await JsonSerializer.DeserializeAsync<AcmeDirectoryResponse>(stream, options);
    }

    public async Task<string> GetTermsOfServiceUrlAsync()
    {
        await ReadDirectoryAsync(new Uri("https://acme-v02.api.letsencrypt.org/directory"));
        return _dir.Meta.TermsOfService;
    }


    public async Task<AcmeAccountResponse> CreateAccountAsync(string key, string email, string nonce, bool termsOfServiceAgreed, string eabKid = null, string eabHmacKey = null)
    {
        var contact = new[] {$"mailto:{email}"};
        var uri = new Uri(_dir.NewAccount);

        if(eabKid == null || eabHmacKey == null)
        {
            var payloadObj = new {contact, termsOfServiceAgreed };
            return await PostAsync<AcmeAccountResponse>(key, uri, payloadObj, nonce);
        }
        
        
        var eabProtected = new 
            {
                alg = "HS256",
                kid = eabKid,
                url = uri
            };
        
        var eabUrlEncoded = _b64.UrlEncode(JsonSerializer.Serialize(eabProtected));

        // Re-use the same JWK as the outer protected section
        var sign = _cert.GetSigner(key);
        var jwk =_cert.GetJwk(sign);
        var innerPayload = _b64.UrlEncode(JsonSerializer.Serialize(jwk));

        var signature = GetSignatureUsingHMAC(eabUrlEncoded + "." + innerPayload, eabHmacKey);            

        var payloadObject = new 
            {
                contact,
                termsOfServiceAgreed,
                externalAccountBinding = new 
                    {
                        @protected = eabUrlEncoded,
                        payload = innerPayload,
                        signature
                    },
            };
        
        return await PostAsync<AcmeAccountResponse>(key, uri, payloadObject, nonce);

        
    }

    private string GetSignatureUsingHMAC(string text, string key)
    {
        var symKey = _b64.UrlDecode(key);

        using var hmacSha256 = new HMACSHA256(symKey);
        var sig = hmacSha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        return _b64.UrlEncode(sig);
    }

    public async Task<AcmeOrderResponse> CreateOrderAsync(string key, string kid, IEnumerable<string> hosts, string nonce)
    {
        var identifiers = hosts.Select(h => new { type = "dns", value = h }).ToArray();
        var payload = new { identifiers };
        var uri = new Uri(_dir.NewOrder);
        return await PostAsync<AcmeOrderResponse>(key, uri, payload, kid, nonce);
    }

    public async Task<string> GetCertAsync(string key, Uri certUri, string kid, string nonce)
    {
        var protectedObject = new { alg = Alg, kid, nonce, url = certUri.AbsoluteUri };
        using var resp = await PostAsync(key, certUri, null, protectedObject);
        await CheckResponseStatusAsync(resp);
        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<AcmeOrderResponse> FinalizeOrderAsync(string key, Uri uri, IEnumerable<string> hosts, string kid, string nonce)
    {
        var csr = _cert.GetCsr(hosts);
        return await PostAsync<AcmeOrderResponse>(key, uri, new { csr }, kid, nonce);
    }

    private async Task<T> PostAsync<T>(string key, Uri uri, object payloadObject, string nonce) where T : AcmeResponse
    {
        var sign = _cert.GetSigner(key);
        var protectedObject = new { alg = Alg, jwk = _cert.GetJwk(sign), nonce, url = uri.AbsoluteUri };
        using var resp = await PostAsync(key, uri, payloadObject, protectedObject);
        return await ParseJsonAsync<T>(resp);
    }

    private async Task<T> PostAsync<T>(string key, Uri uri, object payloadObject, string kid, string nonce) where T : AcmeResponse
    {
        var protectedObject = new { alg = Alg, kid, nonce, url = uri.AbsoluteUri };
        using var resp = await PostAsync(key, uri, payloadObject, protectedObject);
        return await ParseJsonAsync<T>(resp);
    }

    private async Task<HttpResponseMessage> PostAsync(string key, Uri uri, object payloadObject, object protectedObject)
    {
        var payloadJson = payloadObject != null ? JsonSerializer.Serialize(payloadObject) : "";
        var payload = Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));

        var protectedJson = JsonSerializer.Serialize(protectedObject);
        var @protected = Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(protectedJson));

        var toSignbytes = Encoding.UTF8.GetBytes($"{@protected}.{payload}");
        var signatureBytes = _cert.SignData(key, toSignbytes);
        var signature = Base64UrlTextEncoder.Encode(signatureBytes);

        var body = new { @protected, payload, signature };
        var bodyJson = JsonSerializer.Serialize(body);

        var content = new StringContent(bodyJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);

        // Small retry wrapper in case of HTTP failure
        int maxRetries = 3;
        while(_cfg.EnableHttpRetry && maxRetries >= 0)
        {
            try
            {
                return await _http.PostAsync(uri, content);
            }
            catch (Exception e)
            {
                if(maxRetries == 0)
                {
                    throw e;
                }

                _log.LogInformation("HTTP request failed. Retrying in 3 seconds.");
                Thread.Sleep(3000);
            }

            maxRetries--;
        }
        
        // Should never be reached
        return null;
    }

    private static async Task<T> ParseJsonAsync<T>(HttpResponseMessage resp) where T : AcmeResponse
    {
        var content = await GetContentAsync(resp);
        var result = JsonSerializer.Deserialize<T>(content, options);
        result.Nonce = GetHeader(resp, HeaderReplayNonce);
        result.Location = GetHeader(resp, HeaderLocation);
        return result;
    }

    public async Task<string> GetNonceAsync()
    {
        var uri = new Uri(_dir.NewNonce);
        using var message = new HttpRequestMessage(HttpMethod.Head, uri);
        using var resp = await _http.SendAsync(message);

        if (!resp.IsSuccessStatusCode)
        {
            var content = await resp.Content.ReadAsStringAsync();
            throw new Exception($"Unexpected response to get-nonce with status {resp.StatusCode} and content: {content}");
        }

        return GetHeader(resp, HeaderReplayNonce);
    }

    private static async Task<string> GetContentAsync(HttpResponseMessage resp)
    {
        await CheckResponseStatusAsync(resp);
        return await resp.Content.ReadAsStringAsync();
    }

    private static async Task CheckResponseStatusAsync(HttpResponseMessage resp)
    {
        if (!resp.IsSuccessStatusCode)
        {
            var content = await resp.Content.ReadAsStringAsync();
            throw new Exception($"Request failed with status {resp.StatusCode} and content: {content}");
        }
    }

    private static string GetHeader(HttpResponseMessage message, string header)
    {
        if (!message.Headers.TryGetValues(header, out var headers))
        {
            return null;
        }

        return headers.FirstOrDefault();
    }
}
