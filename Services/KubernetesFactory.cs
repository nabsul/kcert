using k8s;
using k8s.Exceptions;

namespace KCert.Services;

[Service]
public class KubernetesFactory(KCertConfig cfg)
{
    public Kubernetes GetClient() => new(GetConfig());

    private KubernetesClientConfiguration GetConfig()
    {
        try
        {
            return KubernetesClientConfiguration.InClusterConfig();
        }
        catch (KubeConfigException)
        {
            return KubernetesClientConfiguration.BuildConfigFromConfigFile(cfg.K8sConfigFile);
        }
    }
}
