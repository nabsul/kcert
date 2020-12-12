using Amazon;
using Amazon.SimpleEmail;
using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace KCert.Lib
{
    public class EmailClient
    {
        private const string CHARSET = "UTF8";

        public bool CanSendEmails(KCertParams p)
        {
            var allFields = new[] { p.AwsKey, p.AwsSecret, p.AwsRegion, p.EmailFrom, p.AcmeEmail };
            return !allFields.Any(string.IsNullOrWhiteSpace);
        }

        public async Task SendAsync(KCertParams p, string subject, string text)
        {
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
    }
}
