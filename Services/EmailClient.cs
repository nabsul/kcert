using KCert.Models;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;

namespace KCert.Services;

[Service]
public class EmailClient
{
    private const string TestSubject = "KCert Test Email";
    private const string TestMessage = "If you received this, then KCert is able to send emails!";

    private readonly ILogger<EmailClient> _log;

    public EmailClient(ILogger<EmailClient> log)
    {
        _log = log;
    }

    public async Task SendTestEmailAsync(KCertParams p)
    {
        _log.LogInformation("Attempting to send a test email.");
        await SendAsync(p, TestSubject, TestMessage);
    }

    public async Task NotifyRenewalResultAsync(KCertParams p, string secretNamespace, string secretName, RenewalException ex)
    {
        await SendAsync(p, RenewalSubject(secretNamespace, secretName, ex), RenewalMessage(secretNamespace, secretName, ex));
    }

    private async Task SendAsync(KCertParams p, string subject, string text)
    {
        if (!CanSendEmails(p))
        {
            _log.LogInformation("Cannot send email email because it's not configured correctly");
            return;
        }

        var client = new SmtpClient(p.SmtpHost, p.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(p.SmtpUser, p.SmtpPass),
        };

        var message = new MailMessage(p.EmailFrom, p.AcmeEmail, subject, text);

        await client.SendMailAsync(message);
    }

    private static bool CanSendEmails(KCertParams p)
    {
        var allFields = new[] { p.SmtpHost, p.SmtpUser, p.SmtpPass, p.EmailFrom, p.AcmeEmail };
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
