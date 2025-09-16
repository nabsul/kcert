using k8s.Models;
using KCert.Services;
using Microsoft.AspNetCore.Mvc;

namespace KCert.Controllers;

[Route("")]
public class HomeController(KCertClient kcert, K8sClient kube, KCertConfig cfg, EmailClient email, CertClient cert) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> HomeAsync(CancellationToken tok)
    {
        var secrets = await kube.GetManagedSecretsAsync(tok).ToListAsync();
        return View(secrets);
    }

    [HttpGet("ingresses")]
    public async Task<IActionResult> IngressesAsync(CancellationToken tok)
    {
        var ingresses = new List<V1Ingress>();
        await foreach (var i in kube.GetAllIngressesAsync(tok))
        {
            ingresses.Add(i);
        }

        return View(ingresses);
    }

    [HttpGet("challenge")]
    public async Task<IActionResult> ChallengeAsync(CancellationToken tok)
    {
        var ingress = await kube.GetIngressAsync(cfg.KCertNamespace, cfg.KCertIngressName, tok);
        return View(ingress);
    }

    [HttpGet("configuration")]
    public IActionResult Configuration()
    {
        ViewBag.TermsOfService = AcmeClient.Dir.Meta.TermsOfService; 
        return View();
    }

    [HttpGet("test-email")]
    public async Task<IActionResult> TestEmailAsync(CancellationToken tok)
    {
        await email.SendTestEmailAsync(tok);
        return RedirectToAction("Configuration");
    }

    [HttpGet("renew/{ns}/{name}")]
    public async Task<IActionResult> RenewAsync(string ns, string name, CancellationToken tok)
    {
        var secret = await kube.GetSecretAsync(ns, name, tok);
        if (secret == null)
        {
            return NotFound();
        }

        var certVal = cert.GetCert(secret);
        var hosts = cert.GetHosts(certVal).ToArray();

        await kcert.StartRenewalProcessAsync(ns, name, hosts, tok);
        return RedirectToAction("Home");
    }
}
