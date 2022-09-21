using System;
using System.Collections.Generic;

namespace KCert;

internal static class Extensions
{
    internal static void ForEach<T>(this IEnumerable<T> items, Action<T> action)
    {
        foreach (var i in items) action(i);
    }
}
