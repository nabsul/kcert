using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace KCert.Controllers
{
    [Route("certificate")]
    public class CertificateController : Controller
    {
        private readonly KCertClient _kcert;
        private readonly K8sClient _kube;
        private readonly CertClient _cert;
        private readonly ILogger<CertificateController> _log;

        public CertificateController(KCertClient kcert, K8sClient kube, CertClient cert, ILogger<CertificateController> log)
        {
            _kcert = kcert;
            _kube = kube;
            _cert = cert;
            _log = log;
        }

        [HttpGet]
        public IActionResult NewAsync() => View("Edit");

        [HttpPost]
        public async Task<IActionResult> CreateAsync(string ns, string name, string[] hosts)
        {
            _log.LogInformation(JsonSerializer.Serialize(new { ns, name, hosts }));
            await _kcert.RenewCertAsync(ns, name, hosts);
            return RedirectToAction("Index", "Home");
        }

        [HttpGet("{ns}/{name}")]
        public async Task<IActionResult> EditAsync(string ns, string name)
        {
            var cert = await _kube.GetSecretAsync(ns, name);
            return View(cert);
        }

        [HttpGet("delete/{ns}/{name}")]
        public async Task<IActionResult> DeleteAsync(string ns, string name)
        {
            await _kube.DeleteSecretAsync(ns, name);
            return RedirectToAction("Index", "Home");
        }

        [HttpPost("{ns}/{name}")]
        public async Task<IActionResult> SaveAsync(string ns, string name, string[] hosts)
        {
            var secret = await _kube.GetSecretAsync(ns, name);
            var cert = _cert.GetCert(secret);
            var currentHosts = _cert.GetHosts(cert);

            // If there's a change, renew the cert
            if (hosts.Length != currentHosts.Count || hosts.Intersect(currentHosts).Count() != hosts.Length)
            {
                await _kcert.RenewCertAsync(ns, name, hosts);
            }

            return RedirectToAction("Index", "Home");
        }
    }
}
