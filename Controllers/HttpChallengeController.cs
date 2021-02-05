using KCert.Lib;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace KCert.Controllers
{
    [Route(".well-known/acme-challenge")]
    public class HttpChallengeController : ControllerBase
    {
        private readonly KCertClient _kcert;
        private readonly ILogger<HttpChallengeController> _log;

        public HttpChallengeController(ILogger<HttpChallengeController> log, KCertClient kcert)
        {
            _log = log;
            _kcert = kcert;
        }

        [HttpGet("{key}")]
        public async Task<IActionResult> GetChallengeResultsAsync(string key)
        {
            _log.LogInformation($"Received ACME Challenge: {key}");
            var thumb = await _kcert.GetThumbprintAsync();
            return Ok($"{key}.{thumb}");
        }
    }
}
