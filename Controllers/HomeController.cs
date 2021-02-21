using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using System.Linq;
using Microsoft.Extensions.Logging;
using System;
using KCert.Services;

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
        public async Task<IActionResult> IndexAsync()
        {
            var secrets = await _kube.GetAllSecretsAsync();
            var ingress = await _kcert.GetKCertIngressAsync();
            var configuredHosts = ingress.Spec.Rules.Select(r => r.Host).ToHashSet();
            var entries = secrets.Select(s => new HomeViewModel(s, configuredHosts, _cert)).ToArray();
            return View(entries);
        }

        [HttpGet("configuration")]
        public async Task<IActionResult> ConfigurationAsync()
        {
            var p = await _kcert.GetConfigAsync();
            return View(p ?? new KCertParams());
        }

        [HttpPost("configuration")]
        public async Task<IActionResult> SaveAsync([FromForm] ConfigurationForm form)
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

        [HttpGet("renew/{ns}/{secretName}")]
        public async Task<IActionResult> RenewAsync(string ns, string secretName)
        {
            try
            {
                await _kcert.GetCertAsync(ns, secretName);
                return RedirectToAction("Index");
            }
            catch (RenewalException ex)
            {
                return View("RenewError", ex);
            }
        }

        [Route("error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
