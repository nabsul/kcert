using KCert.Services;
using Microsoft.AspNetCore.Mvc;

namespace KCert.Controllers;

[Route(".well-known/acme-challenge")]
public class HttpChallengeController : Controller
{
    private readonly CertClient _cert;
    private readonly ILogger<HttpChallengeController> _log;

    public HttpChallengeController(ILogger<HttpChallengeController> log, CertClient cert)
    {
        _log = log;
        _cert = cert;
    }

    [HttpGet("test/{value}")]
    public IActionResult GetTest(string value) => Ok(new { success = true, value });


    [HttpGet("{token}")]
    public IActionResult GetChallengeResults(string token)
    {
        _log.LogInformation("Received ACME Challenge: {token}", token);
        var thumb = _cert.GetThumbprint();
        return Ok($"{token}.{thumb}");
    }
}
