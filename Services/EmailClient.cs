using Amazon;
using Amazon.SimpleEmail;
using KCert.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace KCert.Services
{
    public class EmailClient
    {
        private const string CHARSET = "UTF8";
        private const string TestSubject = "KCert Test Email";
        private const string TestMessage = "If you received this, then KCert is able to send emails!";

        public async Task SendTestEmailAsync(KCertParams p)
        {
            await SendAsync(p, TestSubject, TestMessage);
        }

        public async Task NotifyRenewalResultAsync(KCertParams p, string secretNamespace, string secretName, RenewalException ex)
        {
            await SendAsync(p, RenewalSubject(secretNamespace, secretName, ex), RenewalMessage(secretNamespace, secretName, ex));
        }

        private static async Task SendAsync(KCertParams p, string subject, string text)
        {
            if (!CanSendEmails(p))
            {
                return;
            }

            var region = RegionEndpoint.GetBySystemName(p.AwsRegion);
            var client = new AmazonSimpleEmailServiceClient(p.AwsKey, p.AwsSecret, region);
            var result = await client.SendEmailAsync(new()
            {
                Source = p.EmailFrom,
                Destination = new() { ToAddresses = new() { p.AcmeEmail } },
                Message = new()
                {
                    Subject = new() { Charset = CHARSET, Data = subject },
                    Body = new() { Text = new() { Charset = CHARSET, Data = text } }
                },
            });

            if (result.HttpStatusCode != HttpStatusCode.OK)
            {
                throw new Exception("Error!");
            }
        }

        private static bool CanSendEmails(KCertParams p)
        {
            var allFields = new[] { p.AwsKey, p.AwsSecret, p.AwsRegion, p.EmailFrom, p.AcmeEmail };
            return !allFields.Any(string.IsNullOrWhiteSpace);
        }

        private static string RenewalSubject(string secretNamespace, string secretName, RenewalException ex = null)
        {
            var isSuccess = ex == null;
            var status = isSuccess ? "succeeded" : "failed";
            return $"KCert Renewal of secret [{secretNamespace}:{secretName}] {status}";
        }

        private static string RenewalMessage(string secretNamespace, string secretName, RenewalException ex = null)
        {
            var isSuccess = ex == null;
            var lines = new List<string>() { $"Renewal of secret [{secretNamespace}:{secretName}] completed with status: " + (isSuccess ? "Success" : "Failure") };
            if (!isSuccess)
            {
                lines.Add("\nLogs:\n");
                lines.Add(string.Join('\n', ex.Logs));
                lines.Add($"Error:\n\n{ex.Message}\n\n{ex.StackTrace}");
            }

            return string.Join('\n', lines);
        }

    }
}
