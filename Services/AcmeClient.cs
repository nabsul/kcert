using KCert.Models;
using Microsoft.AspNetCore.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KCert.Services;

public class AcmeClient
{
    private const string HeaderReplayNonce = "Replay-Nonce";
    private const string HeaderLocation = "Location";
    private const string ContentType = "application/jose+json";
    private const string Alg = "ES256";

    private static readonly JsonSerializerOptions options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

    private readonly HttpClient _http = new HttpClient();
    private readonly CertClient _cert;

    public AcmeClient(CertClient cert)
    {
        _cert = cert;
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

    public async Task<AcmeAccountResponse> CreateAccountAsync(string key, string email, string nonce, bool termsAccepted)
    {
        var contact = new[] { $"mailto:{email}" };
        var payloadObject = new { contact, termsOfServiceAgreed = termsAccepted };
        var uri = new Uri(_dir.NewAccount);
        return await PostAsync<AcmeAccountResponse>(key, uri, payloadObject, nonce);
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
        return await _http.PostAsync(uri, content);
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
