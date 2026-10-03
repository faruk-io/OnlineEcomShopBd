using Microsoft.Extensions.Logging;
using TechBazar.Application.Email;

namespace TechBazar.Infrastructure.Email;

/// <summary>
/// Development sender: logs the email instead of sending it. Swap for SMTP/SES/SendGrid in production.
/// Emails contain personal data (name, phone, address, order contents), and logs are retained and shipped around, so at Information level only
/// metadata is written (masked recipient, subject, size). The full text is Debug-only, i.e. visible in Development and never in production defaults.
/// </summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL (log only) to {To} | {Subject} | {Length} chars", MaskEmail(message.To), message.Subject, message.TextBody.Length);
        logger.LogDebug("EMAIL body for {To}:\n{Body}", message.To, message.TextBody);
        return Task.CompletedTask;
    }

    /// <summary>"rahim@example.com" -> "r***@example.com".</summary>
    public static string MaskEmail(string email)
    {
        var at = email.IndexOf('@');
        return at <= 0 ? "***" : $"{email[0]}***{email[at..]}";
    }
}
