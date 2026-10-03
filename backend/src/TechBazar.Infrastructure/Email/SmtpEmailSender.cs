using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TechBazar.Application.Email;

namespace TechBazar.Infrastructure.Email;

public enum SmtpSecurity { Auto, StartTls, SslOnConnect, None }

/// <summary>Bound from <c>Email:Smtp</c>. Leave <see cref="Host"/> empty to keep the log-only sender (development).</summary>
public sealed class SmtpOptions
{
    public const string SectionName = "Email:Smtp";

    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    /// <summary><c>StartTls</c> (587) or <c>SslOnConnect</c> (465) for real providers; <c>None</c> only for a local catcher such as Mailpit.</summary>
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
    public string? Username { get; set; }
    /// <summary>Secret: user-secrets / environment (<c>Email__Smtp__Password</c>), never in a committed file.</summary>
    public string? Password { get; set; }
    public string FromAddress { get; set; } = "";
    public string FromName { get; set; } = "TechBazar BD";
    public int TimeoutSeconds { get; set; } = 15;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);

    /// <summary>
    /// Exactly one mailbox of the form local@domain. MimeKit alone is too lenient for this (it accepts a bare "name" with no domain, and
    /// obsolete route / group syntax), so the shape is checked explicitly.
    /// </summary>
    public static bool IsMailbox(string? value, out MailboxAddress? mailbox)
    {
        mailbox = null;
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) || !MailboxAddress.TryParse(value, out var parsed)) return false;
        var at = parsed.Address.IndexOf('@');
        if (at <= 0 || at != parsed.Address.LastIndexOf('@') || at == parsed.Address.Length - 1 || parsed.Address.Any(char.IsWhiteSpace)) return false;
        mailbox = parsed;
        return true;
    }

    /// <summary>Fail at startup, not on the first password reset: a half-configured sender silently drops mail.</summary>
    public static bool IsValid(SmtpOptions o) =>
        !o.IsConfigured
        || (IsMailbox(o.FromAddress, out _)
            && o.Port is > 0 and <= 65535
            && o.TimeoutSeconds is > 0 and <= 120
            // never send credentials in clear text
            && (string.IsNullOrEmpty(o.Username) || o.Security != SmtpSecurity.None));
}

/// <summary>
/// Sends transactional mail (text + HTML alternative) over SMTP with MailKit. Per the <see cref="IEmailSender"/> contract it never throws for
/// delivery problems (the caller cannot act on them; the order / reset request must not fail). Failures are logged WITHOUT the recipient or
/// the server's reply text (which can echo the address); the full exception is available at Debug level for operators.
/// </summary>
public sealed class SmtpEmailSender(IOptions<SmtpOptions> options, ILogger<SmtpEmailSender> logger) : IEmailSender
{
    private readonly SmtpOptions _o = options.Value;

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        try
        {
            var mime = Build(message);
            using var client = new SmtpClient { Timeout = _o.TimeoutSeconds * 1000 };
            await client.ConnectAsync(_o.Host!, _o.Port, Map(_o.Security), ct);
            if (!string.IsNullOrEmpty(_o.Username)) await client.AuthenticateAsync(_o.Username, _o.Password ?? "", ct);
            await client.SendAsync(mime, ct);
            await client.DisconnectAsync(true, ct);
            logger.LogInformation("EMAIL sent to {To} | {Subject}", LoggingEmailSender.MaskEmail(message.To), message.Subject);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var status = ex is SmtpCommandException c ? c.StatusCode.ToString() : "-";
            logger.LogError("EMAIL to {To} failed: {Error} (smtp status {Status}) | {Subject}", LoggingEmailSender.MaskEmail(message.To), ex.GetType().Name, status, message.Subject);
            logger.LogDebug(ex, "EMAIL failure details");
        }
    }

    private MimeMessage Build(EmailMessage m)
    {
        var mime = new MimeMessage();
        mime.From.Add(new MailboxAddress(_o.FromName, _o.FromAddress));
        // exactly one well-formed mailbox: no header injection or extra recipients through the address
        mime.To.Add(SmtpOptions.IsMailbox(m.To, out var to) ? to! : throw new FormatException("Recipient is not a single valid mailbox."));
        mime.Subject = m.Subject;
        // transactional, machine-generated: tells auto-responders (out-of-office) not to answer
        mime.Headers.Add("Auto-Submitted", "auto-generated");
        mime.Headers.Add("X-Auto-Response-Suppress", "All");
        mime.Body = new BodyBuilder { TextBody = m.TextBody, HtmlBody = m.HtmlBody }.ToMessageBody();
        return mime;
    }

    private static SecureSocketOptions Map(SmtpSecurity s) => s switch
    {
        SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
        SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
        SmtpSecurity.None => SecureSocketOptions.None,
        _ => SecureSocketOptions.Auto,
    };
}
