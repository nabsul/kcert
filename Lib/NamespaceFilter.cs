using System.Text.RegularExpressions;

namespace KCert.Lib
{
    public class NamespaceFilter
    {
        private readonly KCertConfig _cfg;

        public NamespaceFilter(KCertConfig cfg)
        {
            _cfg = cfg;
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
    }
}
