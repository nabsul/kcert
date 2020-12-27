using KCert.Lib;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace KCert.Controllers
{
    [Route("ingress")]
    public class IngressController : Controller
    {
        private readonly KCertClient _kcert;

        public IngressController(KCertClient kcert)
        {
            _kcert = kcert;
        }
        
        [Route("{ns}/{name}")]
        public async Task<IActionResult> IndexAsync(string ns, string name)
        {
            var ingress = await _kcert.GetIngressAsync(ns, name);
            return View(ingress);
        }

        [Route("{ns}/{name}/renew")]
        public async Task<IActionResult> RenewAsync(string ns, string name)
        {
            var result = await _kcert.GetCertAsync(ns, name);
            return View(result);
        }
    }
}
