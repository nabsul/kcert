using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using KCert.Services;

namespace KCert.Controllers;

[Route("")]
public class HomeController : Controller
{
    private readonly KCertClient _kcert;
    private readonly K8sClient _kube;
    private readonly KCertConfig _cfg;
    private readonly EmailClient _email;
    private readonly AcmeClient _acme;

    private static string TermsOfServiceUrl;

    public HomeController(KCertClient kcert, K8sClient kube, KCertConfig cfg, EmailClient email, AcmeClient acme)
    {
        _kcert = kcert;
        _kube = kube;
        _cfg = cfg;
        _email = email;
        _acme = acme;
    }

    [HttpGet("")]
    public async Task<IActionResult> HomeAsync()
    {
        var secrets = await _kube.GetManagedSecretsAsync();
        return View(secrets);
    }

    [HttpGet("challenge")]
    public async Task<IActionResult> ChallengeAsync()
    {
        var ingress = await _kube.GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
        return View(ingress);
    }

    [HttpGet("configuration")]
    public async Task<IActionResult> ConfigurationAsync()
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
        return RedirectToAction("Configuration");
    }

    [HttpGet("renew/{ns}/{name}")]
    public async Task<IActionResult> RenewAsync(string ns, string name)
    {
        await _kcert.RenewCertAsync(ns, name);
        return RedirectToAction("Home");
    }

    [Route("error")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
