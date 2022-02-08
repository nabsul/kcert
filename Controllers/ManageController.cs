using KCert.Services;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace KCert.Controllers;

[Route("manage")]
public class ManageController : Controller
{
    private readonly K8sClient _kube;

    public ManageController(K8sClient kube)
    {
        _kube = kube;
    }

    [HttpGet]
    public async Task<IActionResult> IndexAsync(string op, string ns, string name)
    {
        if (op == "manage")
        {
            await _kube.ManageSecretAsync(ns, name);
            return RedirectToAction();
        }

        var secrets = await _kube.GetUnmanagedSecretsAsync();
        return View(secrets);
    }

    [HttpGet("{ns}/{name}/remove")]
    public async Task<IActionResult> UnmanageAsync(string ns, string name)
    {
        await _kube.UnmanageSecretAsync(ns, name);
        return RedirectToAction("Index", "Home");
    }

    [HttpGet("{ns}/{name}/add")]
    public async Task<IActionResult> ManageAsync(string ns, string name)
    {
        await _kube.ManageSecretAsync(ns, name);
        return RedirectToAction("Index");
    }
}
