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

    [HttpGet("{token}")]
    public IActionResult GetChallengeResults(string token)
    {
        _log.LogInformation("Received ACME Challenge: {token}", token);
        var thumb = _cert.GetThumbprint(token);
        if (thumb == null)
        {
            return NotFound();
        }

        return Ok($"{token}.{thumb}");
    }
}
