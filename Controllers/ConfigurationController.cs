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
        private readonly RenewalManager _renewal;

        public ConfigurationController(KCertClient kcert, EmailClient email, RenewalManager renewal)
        {
            _kcert = kcert;
            _email = email;
            _renewal = renewal;
        }

        [HttpGet]
        [Route("")]
        public async Task<IActionResult> ConfigurationAsync(bool sendEmail = false)
        {
            if (sendEmail)
            {
                await _email.SendTestEmailAsync();
                return RedirectToAction("configuration");
            }

            var p = await _kcert.GetConfigAsync();
            return View(p ?? new KCertParams());
        }

        [HttpPost]
        [Route("")]
        public async Task<IActionResult> SaveConfigurationAsync([FromForm] ConfigurationForm form)
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
            _renewal.RefreshSettings();
            return RedirectToAction("Configuration");
        }
    }
}
