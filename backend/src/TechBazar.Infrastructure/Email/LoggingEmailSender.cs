using Microsoft.Extensions.Logging;
using TechBazar.Application.Email;

namespace TechBazar.Infrastructure.Email;

/// <summary>Development sender: writes the email to the log instead of sending it. Swap for SMTP/SES/SendGrid in production.</summary>
public sealed class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        logger.LogInformation("EMAIL (log only) to {To} | {Subject}\n{Body}", message.To, message.Subject, message.TextBody);
        return Task.CompletedTask;
    }
}
