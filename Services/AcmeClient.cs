using KCert.Models;
using Microsoft.AspNetCore.Authentication;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KCert.Services;

public class AcmeClient(CertClient cert, KCertConfig cfg, ILogger<AcmeClient> log)
{
    private const string HeaderReplayNonce = "Replay-Nonce";
    private const string HeaderLocation = "Location";
    private const string ContentType = "application/jose+json";
    private const string Alg = "ES256";

    private static readonly JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http = new();

    private static AcmeDirectoryResponse? _dir;
    public static AcmeDirectoryResponse Dir => _dir ?? throw new Exception("ACME directory not initialized.");

    private string _nonce = string.Empty;

    private void SaveNonce(HttpResponseMessage resp)
    {
        var nonce = resp.Headers.GetValues(HeaderReplayNonce).FirstOrDefault();
        if (nonce != null)
        {
            _nonce = nonce;
        }
    }

    public Task<AcmeAccountResponse> DeactivateAccountAsync(string key, string kid, CancellationToken tok) => PostAsync<AcmeAccountResponse>(key, new Uri(kid), new { status = "deactivated" }, kid, tok);
    public Task<AcmeOrderResponse> GetOrderAsync(string key, Uri uri, string kid, CancellationToken tok) => PostAsync<AcmeOrderResponse>(key, uri, null, kid, tok);
    public Task<AcmeAuthzResponse> GetAuthzAsync(string key, Uri authzUri, string kid, CancellationToken tok) => PostAsync<AcmeAuthzResponse>(key, authzUri, null, kid, tok);
    public Task<AcmeChallengeResponse> TriggerChallengeAsync(string key, Uri challengeUri, string kid, CancellationToken tok) => PostAsync<AcmeChallengeResponse>(key, challengeUri, new { }, kid, tok);

    public static async Task ReadDirectoryAsync(KCertConfig cfg, CancellationToken tok)
    {
        using var http = new HttpClient();
        using var resp = await http.GetAsync(cfg.AcmeDir, tok);
        if (!resp.IsSuccessStatusCode)
        {
            var result = await resp.Content.ReadAsStringAsync(tok);
            var message = $"Failed to read ACME directory with error response code {resp.StatusCode} and message: {result}";
            throw new Exception(message);
        }

        using var stream = await resp.Content.ReadAsStreamAsync(tok);
        _dir = await JsonSerializer.DeserializeAsync<AcmeDirectoryResponse>(stream, options, tok);
    }

    public async Task<AcmeAccountResponse> CreateAccountAsync(CancellationToken tok)
    {
        var uri = new Uri(Dir.NewAccount);
        var payload = GetAccountRequestPayload(uri);
        return await PostAsync<AcmeAccountResponse>(cfg.AcmeKey, uri, payload, tok);
    }

    private object GetAccountRequestPayload(Uri uri)
    {
        var contact = new[] { $"mailto:{cfg.AcmeEmail}" };

        if (!cfg.UseEabKey)
        {
            return new { contact, termsOfServiceAgreed = cfg.AcmeAccepted };
        }

        var eabProtected = new
        {
            alg = "HS256",
            kid = cfg.AcmeEabKeyId,
            url = uri
        };

        var eabUrlEncoded = Base64UrlTextEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(eabProtected));

        // Re-use the same JWK as the outer protected section
        var sign = cert.GetSigner(cfg.AcmeKey);
        var jwk = cert.GetJwk(sign);
        var innerPayload = Base64UrlTextEncoder.Encode(JsonSerializer.SerializeToUtf8Bytes(jwk));

        var signature = GetSignatureUsingHMAC(eabUrlEncoded + "." + innerPayload, cfg.AcmeHmacKey);

        return new
        {
            contact,
            termsOfServiceAgreed = cfg.AcmeAccepted,
            externalAccountBinding = new
            {
                @protected = eabUrlEncoded,
                payload = innerPayload,
                signature
            },
        };
    }

    private static string GetSignatureUsingHMAC(string text, string key)
    {
        var symKey = Base64UrlTextEncoder.Decode(key);
        using var hmacSha256 = new HMACSHA256(symKey);
        var sig = hmacSha256.ComputeHash(Encoding.UTF8.GetBytes(text));
        return Base64UrlTextEncoder.Encode(sig);
    }

    public async Task<AcmeOrderResponse> CreateOrderAsync(string key, string kid, IEnumerable<string> hosts, CancellationToken tok)
    {
        var identifiers = hosts.Select(h => new { type = "dns", value = h }).ToArray();
        var payload = new { identifiers };
        var uri = new Uri(Dir.NewOrder);
        return await PostAsync<AcmeOrderResponse>(key, uri, payload, kid, tok);
    }

    public async Task<string> GetCertAsync(string key, Uri certUri, string kid, CancellationToken tok)
    {
        var protectedObject = new { alg = Alg, kid, nonce = _nonce, url = certUri.AbsoluteUri };
        using var resp = await PostAsync(key, certUri, null, protectedObject, tok);
        return await GetContentAsync(resp, tok);
    }

    public async Task<AcmeOrderResponse> FinalizeOrderAsync(string key, Uri uri, IEnumerable<string> hosts, string kid, CancellationToken tok)
    {
        var csr = cert.GetCsr(hosts);
        return await PostAsync<AcmeOrderResponse>(key, uri, new { csr }, kid, tok);
    }

    private async Task<T> PostAsync<T>(string key, Uri uri, object payloadObject, CancellationToken tok) where T : AcmeResponse
    {
        var sign = cert.GetSigner(key);
        var protectedObject = new { alg = Alg, jwk = cert.GetJwk(sign), nonce = _nonce, url = uri.AbsoluteUri };
        return await PostAsync<T>(key, uri, payloadObject, protectedObject, tok);
    }

    private async Task<T> PostAsync<T>(string key, Uri uri, object? payloadObject, string kid, CancellationToken tok) where T : AcmeResponse
    {
        var protectedObject = new { alg = Alg, kid, nonce = _nonce, url = uri.AbsoluteUri };
        return await PostAsync<T>(key, uri, payloadObject, protectedObject, tok);
    }

    private async Task<T> PostAsync<T>(string key, Uri uri, object? payloadObject, object protectedObject, CancellationToken tok) where T : AcmeResponse
    {
        using var resp = await PostAsync(key, uri, payloadObject, protectedObject, tok);
        return await ParseJsonAsync<T>(resp, tok);
    }

    private async Task<HttpResponseMessage> PostAsync(string key, Uri uri, object? payloadObject, object protectedObject, CancellationToken tok)
    {
        var payloadJson = payloadObject != null ? JsonSerializer.Serialize(payloadObject) : "";
        var payload = Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));

        var protectedJson = JsonSerializer.Serialize(protectedObject);
        var @protected = Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(protectedJson));

        var toSignbytes = Encoding.UTF8.GetBytes($"{@protected}.{payload}");
        var signatureBytes = cert.SignData(key, toSignbytes);
        var signature = Base64UrlTextEncoder.Encode(signatureBytes);

        var body = new { @protected, payload, signature };
        var bodyJson = JsonSerializer.Serialize(body);

        var content = new StringContent(bodyJson, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);

        return await _http.PostAsync(uri, content, tok);
    }

    private async Task<T> ParseJsonAsync<T>(HttpResponseMessage resp, CancellationToken tok) where T : AcmeResponse
    {
        var content = await GetContentAsync(resp, tok);
        var result = JsonSerializer.Deserialize<T>(content, options) ?? throw new Exception($"Invalid content: {content}");
        SaveNonce(resp);
        if (resp.Headers.TryGetValues(HeaderLocation, out var values) && values.FirstOrDefault() is string loc)
        {
            result.Location = loc;
        }
        return result;
    }

    public async Task InitAsync(CancellationToken tok)
    {
        var uri = new Uri(Dir.NewNonce);
        using var message = new HttpRequestMessage(HttpMethod.Head, uri);
        using var resp = await _http.SendAsync(message, tok);

        if (!resp.IsSuccessStatusCode)
        {
            var content = await resp.Content.ReadAsStringAsync(tok);
            throw new Exception($"Unexpected response to get-nonce with status {resp.StatusCode} and content: {content}");
        }

        SaveNonce(resp);
    }

    private static async Task<string> GetContentAsync(HttpResponseMessage resp, CancellationToken tok)
    {
        var content = await resp.Content.ReadAsStringAsync(tok);
        if (!resp.IsSuccessStatusCode)
        {
            throw new Exception($"Request failed with status {resp.StatusCode} and content: {content}");
        }
        return content;
    }
}
