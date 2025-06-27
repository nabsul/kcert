namespace KCert.Config;

using k8s;
using k8s.Exceptions;

public static class KubernetesFactory
{
    public static Kubernetes GetClient(KCertConfig cfg) => new(GetConfig(cfg));

    private static KubernetesClientConfiguration GetConfig(KCertConfig cfg)
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
