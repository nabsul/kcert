using k8s.Models;
using KCert.Services;
using Microsoft.AspNetCore.Mvc;

namespace KCert.Controllers;

[Route("")]
public class HomeController(KCertClient kcert, K8sClient kube, KCertConfig cfg, EmailClient email, AcmeClient acme, CertClient cert) : Controller
{
    private static string? TermsOfServiceUrl = null;

    [HttpGet("")]
    public async Task<IActionResult> HomeAsync()
    {
        var secrets = await kube.GetManagedSecretsAsync().ToListAsync();
        return View(secrets);
    }

    [HttpGet("ingresses")]
    public async Task<IActionResult> IngressesAsync()
    {
        var ingresses = new List<V1Ingress>();
        await foreach (var i in kube.GetAllIngressesAsync())
        {
            ingresses.Add(i);
        }

        return View(ingresses);
    }

    [HttpGet("challenge")]
    public async Task<IActionResult> ChallengeAsync()
    {
        var ingress = await kube.GetIngressAsync(cfg.KCertNamespace, cfg.KCertIngressName);
        return View(ingress);
    }

    [HttpGet("configuration")]
    public async Task<IActionResult> ConfigurationAsync()
    {
        if (TermsOfServiceUrl == null)
        {
            TermsOfServiceUrl = await acme.GetTermsOfServiceUrlAsync();
        }

        ViewBag.TermsOfService = TermsOfServiceUrl;
        return View();
    }

    [HttpGet("test-email")]
    public async Task<IActionResult> TestEmailAsync()
    {
        await email.SendTestEmailAsync();
        return RedirectToAction("Configuration");
    }

    [HttpGet("renew/{ns}/{name}")]
    public async Task<IActionResult> RenewAsync(string ns, string name)
    {
        var secret = await kube.GetSecretAsync(ns, name);
        if (secret == null)
        {
            return NotFound();
        }

        var certVal = cert.GetCert(secret);
        var hosts = cert.GetHosts(certVal).ToArray();

        await kcert.StartRenewalProcessAsync(ns, name, hosts, CancellationToken.None);
        return RedirectToAction("Home");
    }
}
