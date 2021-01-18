using Amazon;
using Amazon.SimpleEmail;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace KCert.Lib
{
    [Service]
    public class EmailClient
    {
        private const string CHARSET = "UTF8";
        private const string TestSubject = "KCert Test Email";
        private const string TestMessage = "If you received this, then KCert is able to send emails!";

        private readonly KCertClient _kcert;

        public EmailClient(KCertClient kcert)
        {
            _kcert = kcert;
        }

        public async Task SendTestEmailAsync()
        {
            await SendAsync(TestSubject, TestMessage);
        }

        public async Task NotifyRenewalResultAsync(RenewalResult result)
        {
            await SendAsync(RenewalSubject(result), RenewalMessage(result));
        }

        private async Task SendAsync(string subject, string text)
        {
            var p = await _kcert.GetConfigAsync();

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

        private static string RenewalSubject(RenewalResult result)
        {
            var status = result.Success ? "succeeded" : "failed";
            return $"KCert Renewal of ingress [{result.IngressName}] {status}";
        }

        private static string RenewalMessage(RenewalResult result)
        {
            var lines = new[]
            {
                $"Renewal of ingress [{result.IngressNamespace}] [{result.IngressName}] completed with status: " + (result.Success ? "Success" : "Failure"),
                "\nLogs:\n",
                string.Join('\n', result.Logs),
                result.Error == null ? "" : $"Error:\n\n{result.Error.Message}\n\n{result.Error.StackTrace}"
            };

            return string.Join('\n', lines);
        }

    }
}
