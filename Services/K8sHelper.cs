using k8s.Autorest;
using System.Net;

namespace KCert.Services;

public class K8sHelper<T>(IK8sInterface<T> handler) where T : class
{
    public async Task<T?> GetAsync(string ns, string name, CancellationToken tok)
    {
        try
        {
            return await handler.GetAsync(ns, name, tok);
        }
        catch (HttpOperationException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }
    }

    public async Task CreateOrUpdateAsync(string ns, string name, T obj, CancellationToken tok)
    {
        if (await GetAsync(ns, name, tok) is { } prev)
        {
            handler.Update(obj, prev);
            await handler.UpdateAsync(ns, name, prev, tok);
            return;
        }

        await handler.CreateAsync(ns, name, obj, tok);
    }
}
