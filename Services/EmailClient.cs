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
    private readonly KCertConfig _cfg;

    public EmailClient(ILogger<EmailClient> log, KCertConfig cfg)
    {
        _log = log;
        _cfg = cfg;
    }

    public async Task SendTestEmailAsync()
    {
        _log.LogInformation("Attempting to send a test email.");
        await SendAsync(TestSubject, TestMessage);
    }

    public async Task NotifyRenewalResultAsync(string secretNamespace, string secretName, RenewalException ex)
    {
        await SendAsync(RenewalSubject(secretNamespace, secretName, ex), RenewalMessage(secretNamespace, secretName, ex));
    }

    private async Task SendAsync(string subject, string text)
    {
        if (!CanSendEmails())
        {
            _log.LogInformation("Cannot send email email because it's not configured correctly");
            return;
        }

        var client = new SmtpClient(_cfg.SmtpHost, _cfg.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(_cfg.SmtpUser, _cfg.SmtpPass),
        };

        var message = new MailMessage(_cfg.SmtpEmailFrom, _cfg.AcmeEmail, subject, text);

        await client.SendMailAsync(message);
    }

    private bool CanSendEmails()
    {
        var allFields = new[] { _cfg.SmtpHost, _cfg.SmtpUser, _cfg.SmtpPass, _cfg.SmtpEmailFrom, _cfg.AcmeEmail };
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
