using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using KCert.Models;
using System.Threading.Tasks;
using KCert.Lib;

namespace KCert.Controllers
{
    [Route("")]
    public class HomeController : Controller
    {
        private readonly KCertClient _kcert;

        public HomeController(KCertClient kcert)
        {
            _kcert = kcert;
        }

        [HttpGet]
        [Route("")]
        public async Task<IActionResult> IndexAsync()
        {
            var ingresses = await _kcert.GetAllIngressesAsync();
            return View(ingresses);
        }

        [Route("error")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
