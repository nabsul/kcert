using Microsoft.Extensions.Configuration;
using SendGrid;
using SendGrid.Helpers.Mail;
using System.Threading.Tasks;

namespace KCert.Lib
{
    public class EmailClient
    {
        private readonly IConfiguration _cfg;
        private readonly KCertParams _params;

        public EmailClient(IConfiguration cfg, KCertParams p)
        {
            _cfg = cfg;
            _params = p;
        }

        public async Task SendAsync(string fromEmail, string toEmail, string subject, string text)
        {
            var client = new SendGridClient(_params.SendGridKey);
            var from = new EmailAddress { Name = "KCert Bot", Email = fromEmail };
            var to = new EmailAddress { Name = "KCert Human", Email = toEmail };
            var email = MailHelper.CreateSingleEmail(from, to, subject, text, null);
            await client.SendEmailAsync(email);
        }
    }
}
