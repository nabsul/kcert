using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        return View();
    }

    [HttpGet("test-email")]
    public async Task<IActionResult> TestEmailAsync()
    {
        await _email.SendTestEmailAsync();
        return RedirectToAction("Index");
    }
}
