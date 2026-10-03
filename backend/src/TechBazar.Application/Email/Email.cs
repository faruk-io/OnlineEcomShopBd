namespace TechBazar.Application.Email;

public sealed record EmailMessage(string To, string Subject, string TextBody, string? HtmlBody = null);

/// <summary>
/// Outbound email abstraction. Development uses a log-only implementation; plug SMTP / SES / SendGrid in
/// Infrastructure without touching business code. Implementations must not throw for delivery problems the
/// caller cannot act on (the order has already been placed).
/// </summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
