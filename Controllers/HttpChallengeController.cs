using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace KCert.Controllers
{
    [Route(".well-known/acme-challenge")]
    public class HttpChallengeController : ControllerBase
    {
        private readonly KCertClient _kcert;
        private readonly CertClient _cert;
        private readonly ILogger<HttpChallengeController> _log;

        public HttpChallengeController(ILogger<HttpChallengeController> log, KCertClient kcert, CertClient cert)
        {
            _log = log;
            _kcert = kcert;
            _cert = cert;
        }

        [HttpGet("{key}")]
        public async Task<IActionResult> GetChallengeResultsAsync(string key)
        {
            _log.LogInformation($"Received ACME Challenge: {key}");
            var p = await _kcert.GetConfigAsync();
            var thumb = _cert.GetThumbprint(p.AcmeKey);
            return Ok($"{key}.{thumb}");
        }
    }
}
