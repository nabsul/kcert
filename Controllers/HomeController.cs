using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using KCert.Lib;
using System;

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
        public async Task<IActionResult> ConfigurationAsync()
        {
            var p = await _kcert.GetConfigAsync();
            return View(p ?? new KCertParams());
        }

        [HttpPost]
        [Route("configuration")]
        public async Task<IActionResult> SaveConfigurationAsync([FromForm] ConfigurationForm form)
        {
            var p = await _kcert.GetConfigAsync() ?? new KCertParams();

            p.AcmeDirUrl = new Uri(form.AcmeDir);
            p.Email = form.AcmeEmail;
            p.TermsAccepted = form.TermsAccepted;
            if (form.NewKey)
            {
                p.Key = _kcert.GenerateNewKey();
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
