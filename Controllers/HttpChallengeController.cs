﻿using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace KCert.Controllers;

[Route(".well-known/acme-challenge")]
public class HttpChallengeController : ControllerBase
{
    private readonly CertClient _cert;
    private readonly KCertConfig _cfg;
    private readonly ILogger<HttpChallengeController> _log;

    public HttpChallengeController(ILogger<HttpChallengeController> log, CertClient cert, KCertConfig cfg)
    {
        _log = log;
        _cert = cert;
        _cfg = cfg;
    }

    [HttpGet("{key}")]
    public IActionResult GetChallengeResults(string key)
    {
        _log.LogInformation("Received ACME Challenge: {key}", key);
        var thumb = _cert.GetThumbprint(_cfg.AcmeKey);
        return Ok($"{key}.{thumb}");
    }
}
