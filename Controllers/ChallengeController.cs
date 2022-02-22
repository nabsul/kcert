using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace KCert.Controllers;

[Route("challenge")]
public class ChallengeController : Controller
{
    private readonly K8sClient _kube;
    private readonly KCertConfig _cfg;

    public ChallengeController(K8sClient kube, KCertConfig cfg)
    {
        _kube = kube;
        _cfg = cfg;
    }

    [HttpGet]
    public async Task<IActionResult> IndexAsync()
    {
        var ingress = await _kube.GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
        return View(ingress);
    }
}
