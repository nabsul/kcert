using k8s;
using Newtonsoft.Json;
using System.Text.Json.Nodes;

namespace KCert.Services;

public class HttpRouteHandler(Kubernetes k8s) : IK8sInterface<JsonObject>
{
    private const string K8sGroup = "gateway.networking.k8s.io";
    private const string K8sVersion = "v1";
    private const string K8sPluralName = "httproutes";

    public async ListMet<JsonObject> ListAsync(CancellationToken tok)
    {
        await foreach (var obj in k8s.listcustom
        {
            yield return obj;
        }
    }

    public async Task<JsonObject> CreateAsync(string ns, string name, JsonObject obj, CancellationToken tok)
    {
        var res = await k8s.CreateNamespacedCustomObjectAsync(obj, K8sGroup, K8sVersion, ns, K8sPluralName, cancellationToken: tok);
        return Convert(res);
    }

    public async Task DeleteAsync(string ns, string name, CancellationToken tok)
    {
        await k8s.DeleteNamespacedCustomObjectAsync(K8sGroup, K8sVersion, ns, K8sPluralName, name, cancellationToken: tok);
    }

    public async Task<JsonObject> GetAsync(string ns, string name, CancellationToken tok)
    {
        var res = await k8s.GetNamespacedCustomObjectAsync(K8sGroup, K8sVersion, ns, K8sPluralName, name, tok);
        return Convert(res);
    }

    public void Update(JsonObject source, JsonObject target)
    {
        target["spec"] = source["spec"];
    }

    public async Task UpdateAsync(string ns, string name, JsonObject obj, CancellationToken tok)
    {
        await k8s.ReplaceNamespacedCustomObjectAsync(obj, K8sGroup, K8sVersion, ns, K8sPluralName, name, cancellationToken: tok);
    }

    private static JsonObject Convert(object obj)
    {
        var json = obj.ToString() ?? throw new Exception("No JSON provided");
        return JsonConvert.DeserializeObject<JsonObject>(json) ?? throw new Exception("INVALID JSON");
    }
}
