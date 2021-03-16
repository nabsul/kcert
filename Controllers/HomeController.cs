using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using KCert.Services;

namespace KCert.Controllers
{
    [Route("")]
    public class HomeController : Controller
    {
        private readonly KCertClient _kcert;
        private readonly K8sClient _kube;

        public HomeController(KCertClient kcert, K8sClient kube)
        {
            _kcert = kcert;
            _kube = kube;
        }

        [HttpGet]
        public async Task<IActionResult> IndexAsync()
        {
            ViewBag.HostsUpdated = await _kcert.SyncHostsAsync();
            var secrets = await _kube.GetManagedSecretsAsync();
            return View(secrets);
        }

        [HttpGet("renew/{ns}/{name}")]
        public async Task<IActionResult> RenewAsync(string ns, string name)
        {
            await _kcert.RenewCertAsync(ns, name);
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
