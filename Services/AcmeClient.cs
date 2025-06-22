using KCert.Models;
using Microsoft.AspNetCore.Authentication;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KCert.Services;

[Service]
public class AcmeClient(CertClient cert, KCertConfig cfg)
{
    private const string HeaderReplayNonce = "Replay-Nonce";
    private const string HeaderLocation = "Location";
    private const string ContentType = "application/jose+json";
    private const string Alg = "ES256";

    private static readonly JsonSerializerOptions options = new() { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http = new();

    private AcmeDirectoryResponse? _dir = null;

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
        return _dir?.Meta?.TermsOfService ?? throw new Exception("_dir should be defined here");
    }

    public async Task<AcmeAccountResponse> CreateAccountAsync(string nonce)
    {
        var uri = new Uri(_dir?.NewAccount ?? throw new Exception("_dir should be defined here"));
        var payload = GetAccountRequestPayload(uri);
        return await PostAsync<AcmeAccountResponse>(cfg.AcmeKey, uri, payload, nonce);
    }

    private object GetAccountRequestPayload(Uri uri)
    {
        var contact = new[] { $"mailto:{cfg.AcmeEmail}" };

        if (cfg.AcmeEabKeyId == null || cfg.AcmeHmacKey == null)
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

    public async Task<AcmeOrderResponse> CreateOrderAsync(string key, string kid, IEnumerable<string> hosts, string nonce)
    {
        var identifiers = hosts.Select(h => new { type = "dns", value = h }).ToArray();
        var payload = new { identifiers };
        var uri = new Uri(_dir?.NewOrder ?? throw new Exception("_dir should be defined here"));
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
        var csr = cert.GetCsr(hosts);
        return await PostAsync<AcmeOrderResponse>(key, uri, new { csr }, kid, nonce);
    }

    private async Task<T> PostAsync<T>(string key, Uri uri, object payloadObject, string nonce) where T : AcmeResponse
    {
        var sign = cert.GetSigner(key);
        var protectedObject = new { alg = Alg, jwk = cert.GetJwk(sign), nonce, url = uri.AbsoluteUri };
        using var resp = await PostAsync(key, uri, payloadObject, protectedObject);
        return await ParseJsonAsync<T>(resp);
    }

    private async Task<T> PostAsync<T>(string key, Uri uri, object? payloadObject, string kid, string nonce) where T : AcmeResponse
    {
        var protectedObject = new { alg = Alg, kid, nonce, url = uri.AbsoluteUri };
        using var resp = await PostAsync(key, uri, payloadObject, protectedObject);
        return await ParseJsonAsync<T>(resp);
    }

    private async Task<HttpResponseMessage> PostAsync(string key, Uri uri, object? payloadObject, object protectedObject)
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

        return await _http.PostAsync(uri, content);
    }

    private static async Task<T> ParseJsonAsync<T>(HttpResponseMessage resp) where T : AcmeResponse
    {
        var content = await GetContentAsync(resp);
        var result = JsonSerializer.Deserialize<T>(content, options) ?? throw new Exception($"Invalid content: {content}");
        result.Nonce = GetHeader(resp, HeaderReplayNonce);
        result.Location = GetHeader(resp, HeaderLocation);
        return result;
    }

    public async Task<string> GetNonceAsync()
    {
        var uri = new Uri(_dir?.NewNonce ?? throw new Exception("_dir should be defined here"));
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
            throw new Exception($"Header '{header}' not found in response.");
        }

        return headers.First();
    }
}
