using k8s;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace KCert.Services;

public class HttpRouteHandler(Kubernetes k8s) : IK8sInterface<JObject>
{
    private const string K8sGroup = "gateway.networking.k8s.io";
    private const string K8sVersion = "v1";
    private const string K8sPluralName = "httproutes";

    public async IAsyncEnumerable<JObject> ListAsync([EnumeratorCancellation] CancellationToken tok)
    {
        string? continueTok = null;
        do 
        {
            var obj = await k8s.ListClusterCustomObjectAsync(K8sGroup, K8sVersion, K8sPluralName, continueParameter: continueTok, cancellationToken: tok);
            var json = Convert(obj);
            foreach (var item in (JArray)json["items"]!)
            {
                yield return (JObject)item;
            }
            continueTok = json["metadata"]?["continue"]?.ToString();
        } while (continueTok == null);
    }

    public async IAsyncEnumerable<JObject> ListAsync(string ns, [EnumeratorCancellation] CancellationToken tok)
    {
        string? continueTok = null;
        do 
        {
            var obj = await k8s.ListNamespacedCustomObjectAsync(K8sGroup, K8sVersion, ns, K8sPluralName, continueParameter: continueTok, cancellationToken: tok);
            var json = Convert(obj);
            foreach (var item in (JArray)json["items"]!)
            {
                yield return (JObject)item;
            }
            continueTok = json["metadata"]?["continue"]?.ToString();
        } while (continueTok == null);
    }

    public async Task<JObject> CreateAsync(string ns, string name, JObject obj, CancellationToken tok)
    {
        var res = await k8s.CreateNamespacedCustomObjectAsync(obj, K8sGroup, K8sVersion, ns, K8sPluralName, cancellationToken: tok);
        return Convert(res);
    }

    public async Task DeleteAsync(string ns, string name, CancellationToken tok)
    {
        await k8s.DeleteNamespacedCustomObjectAsync(K8sGroup, K8sVersion, ns, K8sPluralName, name, cancellationToken: tok);
    }

    public async Task<JObject> GetAsync(string ns, string name, CancellationToken tok)
    {
        var res = await k8s.GetNamespacedCustomObjectAsync(K8sGroup, K8sVersion, ns, K8sPluralName, name, tok);
        return Convert(res);
    }

    public void Update(JObject source, JObject target)
    {
        target["spec"] = source["spec"];
    }

    public async Task UpdateAsync(string ns, string name, JObject obj, CancellationToken tok)
    {
        await k8s.ReplaceNamespacedCustomObjectAsync(obj, K8sGroup, K8sVersion, ns, K8sPluralName, name, cancellationToken: tok);
    }

    private static JObject Convert(object obj) => JObject.FromObject(obj);
}
