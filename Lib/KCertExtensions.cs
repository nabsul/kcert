using k8s.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace KCert.Lib
{
    public static class KCertExtensions
    {
        private const int PEMLineLen = 64;
        private const string PEMStart = "-----BEGIN PRIVATE KEY-----";
        private const string PEMEnd = "-----END PRIVATE KEY-----";

        private const string AcmePath = "/.well-known/acme-challenge/";
        private const string PathType = "Prefix";

        public static void AddKCertServices(this IServiceCollection services)
        {
            foreach (var type in Assembly.GetEntryAssembly().GetTypes())
            {
                var attr = type.GetCustomAttribute(typeof(ServiceAttribute)) as ServiceAttribute;
                if (attr == null)
                {
                    continue;
                }

                services.AddSingleton(type);
            }
        }

        public static object GetJwk(this ECDsa sign)
        {
            var p = sign.ExportParameters(false);
            return new
            {
                crv = "P-256",
                kty = "EC",
                x = Base64UrlTextEncoder.Encode(p.Q.X),
                y = Base64UrlTextEncoder.Encode(p.Q.Y),
            };
        }

        public static string GetPemKey(this RSA rsa)
        {
            var key = rsa.ExportPkcs8PrivateKey();
            var str = Convert.ToBase64String(key);
            return string.Join('\n', PEMStart, InsertNewLines(str), PEMEnd);
        }

        public static X509Certificate2 Cert(this V1Secret secret) => new X509Certificate2(secret.Data["tls.crt"]);

        public static string SecretName(this Networkingv1beta1Ingress ingress) => ingress.Spec?.Tls?.FirstOrDefault()?.SecretName;

        public static List<string> Hosts(this Networkingv1beta1Ingress ingress) => ingress.Spec.Rules.Select(r => r.Host).ToList();

        public static void AddHttpChallenge(this Networkingv1beta1Ingress ingress, string service, string port)
        {
            foreach (var rule in ingress.Spec.Rules)
            {
                TryAddHttpChallenge(rule, service, port);
            }
        }

        public static void RemoveHttpChallenge(this Networkingv1beta1Ingress ingress)
        {
            foreach (var rule in ingress.Spec.Rules)
            {
                TryRemoveHttpChallenge(rule);
            }
        }

        private static void TryAddHttpChallenge(Networkingv1beta1IngressRule rule, string service, string port)
        {
            rule.Http ??= new Networkingv1beta1HTTPIngressRuleValue();
            rule.Http.Paths ??= new List<Networkingv1beta1HTTPIngressPath>();

            var paths = rule.Http.Paths;
            if (paths.FirstOrDefault()?.Path == AcmePath)
            {
                return;
            }

            var backend = new Networkingv1beta1IngressBackend(serviceName: service, servicePort: port);
            paths.Insert(0, new Networkingv1beta1HTTPIngressPath(backend, AcmePath, PathType));
        }

        private static void TryRemoveHttpChallenge(Networkingv1beta1IngressRule rule)
        {
            var paths = rule?.Http?.Paths;
            if (paths?.FirstOrDefault()?.Path == AcmePath)
            {
                rule.Http.Paths = paths.Skip(1).ToList();
            }
        }

        private static string InsertNewLines(string input) => string.Join('\n', SplitLines(input));

        private static IEnumerable<string> SplitLines(string input)
        {
            int start = 0;
            while (start < input.Length)
            {
                var len = Math.Min(input.Length - start, PEMLineLen);
                yield return input.Substring(start, len);
                start += PEMLineLen;
            }
        }
    }
}
