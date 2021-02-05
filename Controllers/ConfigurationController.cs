using KCert.Lib;
using KCert.Models;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;

namespace KCert.Controllers
{
    [Route("configuration")]
    public class ConfigurationController : Controller
    {
        private readonly KCertClient _kcert;
        private readonly EmailClient _email;

        public ConfigurationController(KCertClient kcert, EmailClient email)
        {
            _kcert = kcert;
            _email = email;
        }

        [HttpGet]
        public async Task<IActionResult> IndexAsync(bool sendEmail = false)
        {
            var p = await _kcert.GetConfigAsync();
            
            if (sendEmail)
            {
                await _email.SendTestEmailAsync(p);
                return RedirectToAction("Index");
            }

            return View(p ?? new KCertParams());
        }

        [HttpPost]
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
    }
}
