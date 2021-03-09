using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using System;
using KCert.Services;
using System.Text.Json;

namespace KCert.Controllers
{
    [Route("")]
    public class HomeController : Controller
    {
        private readonly KCertClient _kcert;
        private readonly K8sClient _kube;
        private readonly CertClient _cert;
        private readonly ILogger<HomeController> _log;

        public HomeController(KCertClient kcert, K8sClient kube, ILogger<HomeController> log, CertClient cert)
        {
            _kcert = kcert;
            _kube = kube;
            _log = log;
            _cert = cert;
        }

        [HttpGet]
        public async Task<IActionResult> IndexAsync(string op, string ns, string name)
        {
            if (op == "renew")
            {
                await _kcert.RenewCertAsync(ns, name);
                return RedirectToAction("Index");
            }

            if (op == "delete")
            {
                await _kube.DeleteSecretAsync(ns, name);
                return RedirectToAction("Index");
            }

            var secrets = await _kube.GetManagedSecretsAsync();
            return View(secrets);
        }

        [HttpGet("new")]
        public IActionResult NewCertAsync() => View("EditCert");

        [HttpPost("new")]
        public async Task<IActionResult> CreateCertAsync(string ns, string name, string[] hosts)
        {
            _log.LogInformation(JsonSerializer.Serialize(new { ns, name, hosts }));
            await _kcert.RenewCertAsync(ns, name, hosts);
            return RedirectToAction("Index");
        }

        [HttpGet("edit/{ns}/{name}")]
        public async Task<IActionResult> EditCertAsync(string ns, string name, string op)
        {
            if (op == "unmanage")
            {
                await _kube.UnmanageSecretAsync(ns, name);
                return RedirectToAction("Index");
            }

            var cert = await _kube.GetSecretAsync(ns, name);
            return View(cert);
        }

        [HttpPost("edit/{ns}/{name}")]
        public async Task<IActionResult> UpdateCertAsync(string ns, string name, string[] hosts)
        {
            var secret = await _kube.GetSecretAsync(ns, name);
            var cert = _cert.GetCert(secret);
            var currentHosts = _cert.GetHosts(cert);
            
            // If there's a change, renew the cert
            if (hosts.Length != currentHosts.Count || hosts.Intersect(currentHosts).Count() != hosts.Length)
            {
                await _kcert.RenewCertAsync(ns, name, hosts);
            }

            return RedirectToAction("Index");
        }

        [HttpGet("challenge")]
        public async Task<IActionResult> ChallengeIngressAsync(string op)
        {
            if (op == "refresh")
            {
                await _kcert.SyncHostsAsync();
                return RedirectToAction();
            }

            var ingress = await _kcert.GetKCertIngressAsync();
            return View(ingress);
        }

        [HttpGet("manage")]
        public async Task<IActionResult> ManageSecretsAsync(string op, string ns, string name)
        {
            if (op == "manage")
            {
                await _kube.ManageSecretAsync(ns, name);
                return RedirectToAction();
            }

            var secrets = await _kube.GetUnmanagedSecretsAsync();
            return View(secrets);
        }

        [HttpGet("configuration")]
        public async Task<IActionResult> ConfigurationAsync()
        {
            var p = await _kcert.GetConfigAsync();
            return View(p ?? new KCertParams());
        }

        [HttpPost("configuration")]
        public async Task<IActionResult> SaveConfigAsync([FromForm] ConfigurationForm form)
        {
            var p = await _kcert.GetConfigAsync() ?? new KCertParams();

            p.AcmeDirUrl = new Uri(form.AcmeDir);
            p.AcmeEmail = form.AcmeEmail;
            p.EnableAutoRenew = form.EnableAutoRenew;
            p.AwsRegion = form.AwsRegion;
            p.AwsKey = form.AwsKey;
            p.AwsSecret = form.AwsSecret;
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
                p.AcmeKey = _cert.GenerateNewKey();
            }

            await _kcert.SaveConfigAsync(p);
            return RedirectToAction("Configuration");
        }

        [HttpPost("challenge")]
        public async Task<IActionResult> SyncChallengeHostsAsync()
        {
            await _kcert.SyncHostsAsync();
            return RedirectToAction("Index");
        }

        [Route("error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
