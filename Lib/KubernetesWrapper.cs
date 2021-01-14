using k8s;
using Microsoft.Extensions.Configuration;

namespace KCert.Lib
{
    [Service(Type = typeof(Kubernetes))]
    public class KubernetesWrapper : Kubernetes
    {
        public KubernetesWrapper(IConfiguration cfg) : base(GetConfig(cfg))
        {

        }

        private static KubernetesClientConfiguration GetConfig(IConfiguration cfg)
        {
            var file = cfg["Config"];
            return string.IsNullOrWhiteSpace(file)
                ? KubernetesClientConfiguration.InClusterConfig()
                : KubernetesClientConfiguration.BuildConfigFromConfigFile(file);
        }
    }
}
