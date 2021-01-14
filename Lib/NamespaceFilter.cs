using k8s.Models;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace KCert.Lib
{
    [Service]
    public class NamespaceFilter
    {
        private readonly KCertConfig _cfg;
        private readonly ILogger<NamespaceFilter> _log;

        public NamespaceFilter(KCertConfig cfg, ILogger<NamespaceFilter> log)
        {
            _cfg = cfg;
            _log = log;
        }

        public bool IsManagedNamespace(string ns)
        {
            foreach(var f in _cfg.ManagedNamespaces)
            {
                var matchResult = true;
                var filter = f;
                if (f.StartsWith('!'))
                {
                    matchResult = false;
                    filter = f.Substring(1);
                }

                filter = filter.Replace("*", ".*").Replace("?", ".");
                var regex = $"^{filter}$";
                if (Regex.IsMatch(ns, regex))
                {
                    return matchResult;
                }
            }

            return false;
        }

        public bool IsManagedIngress(Networkingv1beta1Ingress ingress)
        {
            return !IsKCertIngress(ingress) && IsManagedNamespace(ingress.Namespace());
        }

        public bool IsKCertIngress(Networkingv1beta1Ingress ingress)
        {
            return ingress.Name() == _cfg.KCertIngressName && ingress.Namespace() == _cfg.KCertNamespace;
        }
    }
}
