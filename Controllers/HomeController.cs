using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using KCert.Lib;
using System;
using System.Linq;

namespace KCert.Controllers
{
    public class HomeController : Controller
    {
        private readonly KCertClient _kcert;

        public HomeController(KCertClient kcert)
        {
            _kcert = kcert;
        }

        public async Task<IActionResult> IndexAsync()
        {
            var ingresses = await _kcert.GetAllIngressesAsync();
            return View(ingresses);
        }

        [HttpGet]
        [Route("configuration")]
        public async Task<IActionResult> ConfigurationAsync(bool sendEmail = false)
        {
            var p = await _kcert.GetConfigAsync();

            if (!new[] { p.AwsKey, p.AwsSecret, p.EmailFrom }.All(string.IsNullOrWhiteSpace) && sendEmail)
            {
                var email = new EmailClient(p);
                await email.SendAsync("This is a test", "Test test\n\n123");
                return RedirectToAction("configuration");
            }

            return View(p ?? new KCertParams());
        }

        [HttpPost]
        [Route("configuration")]
        public async Task<IActionResult> SaveConfigurationAsync([FromForm] ConfigurationForm form)
        {
            var p = await _kcert.GetConfigAsync() ?? new KCertParams();

            p.AcmeDirUrl = new Uri(form.AcmeDir);
            p.AcmeEmail = form.AcmeEmail;
            p.EnableAutoRenew = form.EnableAutoRenew;
            p.AwsRegion = form.AwsRegion;
            p.AwsKey = form.AwsKey;
            p.AwsSecret= form.AwsSecret;
            p.EmailFrom = form.EmailFrom;
            p.TermsAccepted = form.TermsAccepted;
            
            if (form.NewKey)
            {
                p.AcmeKey = _kcert.GenerateNewKey();
            }
            
            await _kcert.SaveConfigAsync(p);
            return RedirectToAction("Configuration");
        }

        [Route("ingress/{ns}/{name}")]
        public async Task<IActionResult> ViewAsync(string ns, string name)
        {
            var ingress = await _kcert.GetIngressAsync(ns, name);
            return View(ingress);
        }

        [Route("ingress/{ns}/{name}/renew")]
        public async Task<IActionResult> RenewAsync(string ns, string name)
        {
            var result = await _kcert.GetCertAsync(ns, name);
            return View(result);
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
