using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace KCert.Controllers;

[Route("challenge")]
public class ChallengeController : Controller
{
    private readonly KCertClient _kcert;
    private readonly K8sClient _kube;
    private readonly KCertConfig _cfg;

    public ChallengeController(KCertClient kcert, K8sClient kube, KCertConfig cfg)
    {
        _kcert = kcert;
        _kube = kube;
        _cfg = cfg;
    }

    [HttpGet]
    public async Task<IActionResult> IndexAsync()
    {
        var ingress = await _kube.GetIngressAsync(_cfg.KCertNamespace, _cfg.KCertIngressName);
        return View(ingress);
    }

    [HttpGet("refresh")]
    public async Task<IActionResult> RefreshAsync()
    {
        await _kcert.SyncHostsAsync();
        return RedirectToAction("Index");
    }
}
