using KCert.Services;
using Microsoft.AspNetCore.Mvc;

namespace KCert.Controllers;

[Route(".well-known/acme-challenge")]
public class HttpChallengeController(ILogger<HttpChallengeController> log, CertClient cert) : Controller
{

    [HttpGet("{token}")]
    public IActionResult GetChallengeResults(string token)
    {
        log.LogInformation("Received ACME Challenge: {token}", token);
        var thumb = cert.GetThumbprint();
        return Ok($"{token}.{thumb}");
    }
}
