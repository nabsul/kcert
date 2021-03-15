using KCert.Models;
using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace KCert.Controllers
{
    [Route("configuration")]
    public class ConfigurationController : Controller
    {
        private readonly KCertClient _kcert;
        private readonly EmailClient _email;
        private readonly CertClient _cert;
        private readonly ILogger<ConfigurationController> _log;

        public ConfigurationController(KCertClient kcert, EmailClient email, CertClient cert, ILogger<ConfigurationController> log)
        {
            _kcert = kcert;
            _email = email;
            _cert = cert;
            _log = log;
        }

        [HttpGet]
        public async Task<IActionResult> IndexAsync(bool sendEmail = false)
        {
            var p = await _kcert.GetConfigAsync();
            return View(p ?? new KCertParams());
        }

        [HttpGet("test-email")]
        public async Task<IActionResult> TestEmailAsync()
        {
            var p = await _kcert.GetConfigAsync();
            await _email.SendTestEmailAsync(p);
            return RedirectToAction("Index");
        }


        [HttpPost]
        public async Task<IActionResult> SaveAsync([FromForm] ConfigurationForm form)
        {
            var p = await _kcert.GetConfigAsync() ?? new KCertParams();

            p.AcmeDirUrl = new Uri(form.AcmeDir);
            p.AcmeEmail = form.AcmeEmail;
            p.EnableAutoRenew = form.EnableAutoRenew;
            p.SmtpHost = form.SmtpHost;
            p.SmtpPort = form.SmtpPort;
            p.SmtpUser = form.SmtpUser;
            p.SmtpPass = form.SmtpPass;
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
            return RedirectToAction("Index");
        }
    }
}
