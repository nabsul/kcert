using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using KCert.Lib;
using k8s.Models;
using System.Collections.Generic;
using System.Linq;

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
        [Route("")]
        public async Task<IActionResult> IndexAsync()
        {
            var ingresses = await _kcert.GetAllIngressesAsync();
            return View(await GetViewModelsAsync(ingresses));
        }

        private async Task<List<HomeViewModel>> GetViewModelsAsync(IEnumerable<Networkingv1beta1Ingress> ingresses)
        {
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
                    });
                }
            }

            return result;
        }

        [HttpGet]
        [Route("challenge")]
        public async Task<IActionResult> ChallengeAsync()
        {
            var ingress = await _kcert.GetKCertIngressAsync();
            return View(ingress);
        }

        [HttpPost]
        [Route("challenge")]
        public async Task<IActionResult> SyncChallengeHostsAsync()
        {
            await _kcert.SyncHostsAsync();
            return RedirectToAction("Challenge");
        }

        [Route("renew/{ns}/{secretName}")]
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
