using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using KCert.Lib;
using k8s.Models;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using System;

namespace KCert.Controllers
{
    [Route("")]
    public class HomeController : Controller
    {
        private readonly KCertClient _kcert;
        private readonly K8sClient _kube;
        private readonly EmailClient _email;
        private readonly ILogger<HomeController> _log;

        public HomeController(KCertClient kcert, K8sClient kube, ILogger<HomeController> log, EmailClient email)
        {
            _kcert = kcert;
            _kube = kube;
            _log = log;
            _email = email;
        }

        [HttpGet]
        public async Task<IActionResult> IndexAsync()
        {
            var ingresses = await _kcert.GetAllIngressesAsync();
            var kcertIngress = await _kcert.GetKCertIngressAsync();
            var hosts = kcertIngress.Spec.Rules.Select(r => r.Host).ToHashSet();
            _log.LogInformation($"kcert hosts: {string.Join(";", hosts)}");
            var result = new List<HomeViewModel>();
            
            foreach (var i in ingresses)
            {
                foreach (var tls in i.Spec.Tls)
                {
                    var s = tls.SecretName == null ? null : await _kube.GetSecretAsync(i.Namespace(), i.SecretName());
                    result.Add(new HomeViewModel
                    {
                        Namespace = i.Namespace(),
                        IngressName = i.Name(),
                        SecretName = tls.SecretName,
                        Hosts = tls.Hosts.ToArray(),
                        Created = s?.Cert()?.NotBefore,
                        Expires = s?.Cert()?.NotAfter,
                        HasChallengeEntry = tls.Hosts.All(h => hosts.Contains(h)),
                    });
                }
            }

            return View((result, kcertIngress));
        }

        [HttpGet("configuration")]
        public async Task<IActionResult> ConfigurationAsync(bool sendEmail = false)
        {
            var p = await _kcert.GetConfigAsync();

            if (sendEmail)
            {
                await _email.SendTestEmailAsync(p);
                return RedirectToAction("Index");
            }

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

            if (form.NewKey)
            {
                p.AcmeKey = _kcert.GenerateNewKey();
            }

            await _kcert.SaveConfigAsync(p);
            return RedirectToAction("Index");
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
            var result = await _kcert.GetCertAsync(ns, secretName);
            return View(result);
        }

        [Route("error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
