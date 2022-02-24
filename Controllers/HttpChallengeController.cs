using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace KCert.Controllers;

[Route(".well-known/acme-challenge")]
public class HttpChallengeController : ControllerBase
{
    private readonly CertClient _cert;
    private readonly ILogger<HttpChallengeController> _log;

    public HttpChallengeController(ILogger<HttpChallengeController> log, CertClient cert)
    {
        _log = log;
        _cert = cert;
    }

    [HttpGet("{key}")]
    public IActionResult GetChallengeResults(string key)
    {
        _log.LogInformation("Received ACME Challenge: {key}", key);
        var thumb = _cert.GetThumbprint(key);
        if (thumb == null)
        {
            return NotFound();
        }

        return Ok($"{key}.{thumb}");
    }
}
