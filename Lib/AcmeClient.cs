using KCert.Lib.Acme;
using Microsoft.AspNetCore.Authentication;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KCert.Lib
{
    [Service]
    public class AcmeClient
    {
        private const string DirFieldNewNonce = "newNonce";
        private const string DirFieldNewAccount = "newAccount";
        private const string DirFieldNewOrder = "newOrder";

        private const string HeaderReplayNonce = "Replay-Nonce";
        private const string HeaderLocation = "Location";
        private const string ContentType = "application/jose+json";
        private const string Alg = "ES256";

        private readonly HttpClient _http;
        private JsonDocument _dir;

        public AcmeClient()
        {
            _http = new HttpClient();
        }

        public Task<AcmeResponse> DeactivateAccountAsync(ECDsa sign, string kid, string nonce) => PostAsync(sign, new Uri(kid), new { status = "deactivated" }, kid, nonce);
        public Task<AcmeResponse> GetOrderAsync(ECDsa sign, Uri uri, string kid, string nonce) => PostAsync(sign, uri, null, kid, nonce);
        public Task<AcmeResponse> GetAuthzAsync(ECDsa sign, Uri authzUri, string kid, string nonce) => PostAsync(sign, authzUri, null, kid, nonce);
        public Task<AcmeResponse> TriggerChallengeAsync(ECDsa sign, Uri challengeUri, string kid, string nonce) => PostAsync(sign, challengeUri, new { }, kid, nonce);

        public async Task ReadDirectoryAsync(Uri dirUri)
        {
            using var resp = await _http.GetAsync(dirUri);
            _dir = await GetJsonAsync(resp);
        }

        public async Task<AcmeResponse> CreateAccountAsync(ECDsa sign, string email, string nonce)
        {
            var contact = new[] { $"mailto:{email}" };
            var payloadObject = new { contact, termsOfServiceAgreed = true };
            var uri = GetFromDirectory(DirFieldNewAccount);
            return await PostAsync(sign, uri, payloadObject, nonce);
        }

        public async Task<AcmeResponse> CreateOrderAsync(ECDsa sign, string kid, IEnumerable<string> hosts, string nonce)
        {
            var identifiers = hosts.Select(h => new { type = "dns", value = h }).ToArray();
            var payload = new { identifiers };
            var uri = GetFromDirectory(DirFieldNewOrder);
            return await PostAsync(sign, uri, payload, kid, nonce);
        }

        public async Task<string> GetCertAsync(ECDsa sign, Uri certUri, string kid, string nonce)
        {
            var protectedObject = new { alg = Alg, kid, nonce, url = certUri.AbsoluteUri };
            using var resp = await PostAsync(sign, certUri, null, protectedObject);
            await CheckResponseStatusAsync(resp);
            return await resp.Content.ReadAsStringAsync();
        }

        public async Task<AcmeResponse> FinalizeOrderAsync(RSA rsa, ECDsa sign, Uri uri, string domain, string kid, string nonce)
        {
            var req = new CertificateRequest($"CN={domain}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var signed = req.CreateSigningRequest();
            var csr = Base64UrlTextEncoder.Encode(signed);
            return await PostAsync(sign, uri, new { csr }, kid, nonce);
        }

        private async Task<AcmeResponse> PostAsync(ECDsa sign, Uri uri, object payloadObject, string nonce)
        {
            var protectedObject = new { alg = Alg, jwk = sign.GetJwk(), nonce, url = uri.AbsoluteUri };
            using var resp = await PostAsync(sign, uri, payloadObject, protectedObject);
            return await ParseJsonAsync(resp);
        }

        private async Task<AcmeResponse> PostAsync(ECDsa sign, Uri uri, object payloadObject, string kid, string nonce)
        {
            var protectedObject = new { alg = Alg, kid, nonce, url = uri.AbsoluteUri };
            using var resp = await PostAsync(sign, uri, payloadObject, protectedObject);
            return await ParseJsonAsync(resp);
        }

        private async Task<HttpResponseMessage> PostAsync(ECDsa sign, Uri uri, object payloadObject, object protectedObject)
        {
            var payloadJson = payloadObject != null ? JsonSerializer.Serialize(payloadObject) : "";
            var payload = Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(payloadJson));

            var protectedJson = JsonSerializer.Serialize(protectedObject);
            var @protected = Base64UrlTextEncoder.Encode(Encoding.UTF8.GetBytes(protectedJson));

            var toSignbytes = Encoding.UTF8.GetBytes($"{@protected}.{payload}");
            var signatureBytes = sign.SignData(toSignbytes, HashAlgorithmName.SHA256);
            var signature = Base64UrlTextEncoder.Encode(signatureBytes);

            var body = new { @protected, payload, signature };
            var bodyJson = JsonSerializer.Serialize(body);

            var content = new StringContent(bodyJson, Encoding.UTF8);
            content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
            return await _http.PostAsync(uri, content);
        }

        private static async Task<AcmeResponse> ParseJsonAsync(HttpResponseMessage resp)
        {
            var json = await GetJsonAsync(resp);
            return new AcmeResponse
            {
                Location = GetHeader(resp, HeaderLocation),
                Nonce = GetHeader(resp, HeaderReplayNonce),
                Content = json,
            };
        }

        public async Task<string> GetNonceAsync()
        {
            var uri = GetFromDirectory(DirFieldNewNonce);
            using var message = new HttpRequestMessage(HttpMethod.Head, uri);
            using var resp = await _http.SendAsync(message);

            if (!resp.IsSuccessStatusCode)
            {
                var content = await resp.Content.ReadAsStringAsync();
                throw new Exception($"Unexpected response to get-nonce with status {resp.StatusCode} and content: {content}");
            }

            return GetHeader(resp, HeaderReplayNonce);
        }

        private Uri GetFromDirectory(string field)
        {
            if (_dir != null && _dir.RootElement.TryGetProperty(field, out var val))
            {
                return new Uri(val.GetString());
            }

            throw new KeyNotFoundException($"AcmeClient::GetFromDirectory: Key not found: {field}");
        }

        private static async Task<JsonDocument> GetJsonAsync(HttpResponseMessage resp)
        {
            await CheckResponseStatusAsync(resp);
            using var stream = await resp.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
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
}
