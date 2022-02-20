﻿using KCert.Models;
using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace KCert.Controllers;

[Route("configuration")]
public class ConfigurationController : Controller
{
    private readonly KCertClient _kcert;
    private readonly EmailClient _email;
    private readonly CertClient _cert;
    private readonly AcmeClient _acme;
    private readonly ILogger<ConfigurationController> _log;

    private static string TermsOfServiceUrl;

    public ConfigurationController(KCertClient kcert, EmailClient email, CertClient cert, ILogger<ConfigurationController> log, AcmeClient acme)
    {
        _kcert = kcert;
        _email = email;
        _cert = cert;
        _log = log;
        _acme = acme;
    }

    [HttpGet]
    public async Task<IActionResult> IndexAsync()
    {
        if (TermsOfServiceUrl == null)
        {
            TermsOfServiceUrl = await _acme.GetTermsOfServiceUrlAsync();
        }

        ViewBag.TermsOfService = TermsOfServiceUrl;
        var p = await _kcert.GetConfigAsync();
        return View(p ?? new KCertParams());
    }

    [HttpGet("test-email")]
    public async Task<IActionResult> TestEmailAsync()
    {
        var p = await _kcert.GetConfigAsync();
        await _email.SendTestEmailAsync(p);
        return RedirectToAction("Index");
    }


    [HttpPost]
    public async Task<IActionResult> SaveAsync([FromForm] ConfigurationForm form)
    {
        var p = await _kcert.GetConfigAsync() ?? new KCertParams();

        p.AcmeDirUrl = new Uri(form.AcmeDir);
        p.AcmeEmail = form.AcmeEmail;
        p.EnableAutoRenew = form.EnableAutoRenew;
        p.SmtpHost = form.SmtpHost;
        p.SmtpPort = form.SmtpPort;
        p.SmtpUser = form.SmtpUser;
        p.SmtpPass = form.SmtpPass;
        p.EmailFrom = form.EmailFrom;
        p.TermsAccepted = form.TermsAccepted;

        if (!(form?.AcmeKey?.All(c => c == '*') ?? false))
        {
            _log.LogInformation("Setting key value from form");
            p.AcmeKey = form.AcmeKey;
        }

        if (string.IsNullOrWhiteSpace(p.AcmeKey))
        {
            _log.LogInformation("Generating new key");
            p.AcmeKey = CertClient.GenerateNewKey();
        }

        await _kcert.SaveConfigAsync(p);
        return RedirectToAction("Index");
    }
}
