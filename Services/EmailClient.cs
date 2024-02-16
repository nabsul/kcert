using KCert.Models;
using System.Net;
using System.Net.Mail;

namespace KCert.Services;

[Service]
public class EmailClient(ILogger<EmailClient> log, KCertConfig cfg)
{
    private const string TestSubject = "KCert Test Email";
    private const string TestMessage = "If you received this, then KCert is able to send emails!";

    public async Task SendTestEmailAsync()
    {
        log.LogInformation("Attempting to send a test email.");
        await SendAsync(TestSubject, TestMessage);
    }

    public async Task NotifyRenewalResultAsync(string secretNamespace, string secretName, RenewalException? ex)
    {
        await SendAsync(RenewalSubject(secretNamespace, secretName, ex), RenewalMessage(secretNamespace, secretName, ex));
    }

    public async Task NotifyFailureAsync(string message, Exception ex)
    {
        var subject = "KCert encountered an unexpected error";
        var body = $"{message}\n\n{ex.Message}\n\n{ex.StackTrace}";
        await SendAsync(subject, body);
    }

    private async Task SendAsync(string subject, string text)
    {
        if (cfg.SmtpHost == null || cfg.SmtpUser == null || cfg.SmtpPass == null || cfg.SmtpEmailFrom == null)
        {
            log.LogInformation("Cannot send email email because it's not configured correctly");
            return;
        }

        var client = new SmtpClient(cfg.SmtpHost, cfg.SmtpPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(cfg.SmtpUser, cfg.SmtpPass),
        };

        var message = new MailMessage(cfg.SmtpEmailFrom, cfg.AcmeEmail, subject, text);

        await client.SendMailAsync(message);
    }

    private static string RenewalSubject(string secretNamespace, string secretName, RenewalException? ex = null)
    {
        var isSuccess = ex == null;
        var status = isSuccess ? "succeeded" : "failed";
        return $"KCert Renewal of secret [{secretNamespace}:{secretName}] {status}";
    }

    private static string RenewalMessage(string secretNamespace, string secretName, RenewalException? ex = null)
    {
        var lines = new List<string>() { $"Renewal of secret [{secretNamespace}:{secretName}] completed with status: " + (ex == null ? "Success" : "Failure") };
        if (ex != null)
        {
            lines.Add("\nLogs:\n");
            lines.Add(string.Join('\n', ex.Logs));
            lines.Add($"Error:\n\n{ex.Message}\n\n{ex.StackTrace}");
        }

        return string.Join('\n', lines);
    }
}
