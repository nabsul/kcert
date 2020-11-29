using Amazon;
using Amazon.SimpleEmail;
using System;
using System.Net;
using System.Threading.Tasks;

namespace KCert.Lib
{
    public class EmailClient
    {
        private const string CHARSET = "UTF8";
        private readonly KCertParams _params;

        public EmailClient(KCertParams p)
        {
            _params = p;
        }

        public async Task SendAsync(string subject, string text)
        {
            var client = new AmazonSimpleEmailServiceClient(_params.AwsKey, _params.AwsSecret, _params.AwsRegion);
            var result = await client.SendEmailAsync(new()
            {
                Source = _params.EmailFrom,
                Destination = new() { ToAddresses = new() { _params.AcmeEmail } },
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
