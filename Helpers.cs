using k8s.Models;
using k8s;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace KCert;

public static class Helpers
{
    public static async IAsyncEnumerable<TItem> K8sEnumerateAsync<TItem, TList>(Func<string, Task<TList>> callback) where TList: IKubernetesObject<V1ListMeta>, IItems<TItem>
    {
        string tok = null;
        do
        {
            var result = await callback(tok);
            tok = result.Continue();
            foreach (var i in result.Items) yield return i;
        }
        while (tok != null);
    }

    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> enumerable)
    {
        var list = new List<T>();
        await foreach (var item in enumerable) list.Add(item);
        return list;
    }
}
