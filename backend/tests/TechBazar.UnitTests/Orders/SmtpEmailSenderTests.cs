using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using TechBazar.Application.Email;
using TechBazar.Infrastructure;
using TechBazar.Infrastructure.Email;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Orders;

public class SmtpEmailSenderTests
{
    private static SmtpEmailSender Sender(int port, CapturingLogger<SmtpEmailSender> log, Action<SmtpOptions>? tweak = null)
    {
        var o = new SmtpOptions { Host = "127.0.0.1", Port = port, Security = SmtpSecurity.None, FromAddress = "no-reply@techbazar.example", FromName = "TechBazar BD", TimeoutSeconds = 5 };
        tweak?.Invoke(o);
        return new SmtpEmailSender(Options.Create(o), log);
    }

    private static readonly EmailMessage Mail = new("rahim@example.com", "Reset your password - TechBazar BD",
        "Hi Rahim,\nhttps://shop.example/reset-password#token=abc", "<p>Hi Rahim,</p><p><a href=\"https://shop.example/reset-password#token=abc\">Choose a new password</a></p>");

    [Fact]
    public async Task DeliversAProperMultipartMessageWithTheAutoReplySuppressionHeaders()
    {
        using var server = new FakeSmtpServer();
        var log = new CapturingLogger<SmtpEmailSender>();
        await Sender(server.Port, log).SendAsync(Mail);

        var raw = Assert.Single(server.Messages);
        var mime = MimeMessage.Load(new MemoryStream(raw));
        Assert.Equal("no-reply@techbazar.example", mime.From.Mailboxes.Single().Address);
        Assert.Equal("TechBazar BD", mime.From.Mailboxes.Single().Name);
        Assert.Equal("rahim@example.com", mime.To.Mailboxes.Single().Address);
        Assert.Equal(Mail.Subject, mime.Subject);
        Assert.Contains("#token=abc", mime.TextBody);
        Assert.Contains("Choose a new password", mime.HtmlBody);
        Assert.Equal("auto-generated", mime.Headers["Auto-Submitted"]);
        Assert.Equal("All", mime.Headers["X-Auto-Response-Suppress"]);
        Assert.Single(log.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("r***@example.com"));
    }

    [Fact]
    public async Task ARejectingServerNeverThrows_AndNeitherTheAddressNorTheServerReplyReachTheLogs()
    {
        using var server = new FakeSmtpServer { RejectRecipients = true };
        var log = new CapturingLogger<SmtpEmailSender>();
        await Sender(server.Port, log).SendAsync(Mail);              // no exception: callers (orders, resets) must not fail

        Assert.Empty(server.Messages);
        var visible = string.Join("\n", log.Entries.Where(e => e.Level >= LogLevel.Information).Select(e => e.Message));
        Assert.Contains("failed", visible);
        Assert.Contains("r***@example.com", visible);
        Assert.DoesNotContain("rahim@example.com", visible);       // the 550 reply echoes the address; it must not be logged at Information+
        Assert.DoesNotContain("user unknown", visible);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Debug);   // operators still get the details when they ask
    }

    [Fact]
    public async Task AnUnreachableServerNeverThrows()
    {
        int closedPort;
        using (var probe = new FakeSmtpServer()) closedPort = probe.Port;     // port that was open a moment ago and is closed now
        var log = new CapturingLogger<SmtpEmailSender>();
        await Sender(closedPort, log).SendAsync(Mail);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error && e.Message.Contains("failed"));
    }

    [Fact]
    public async Task CancellationIsHonoured()
    {
        using var server = new FakeSmtpServer();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sender(server.Port, new CapturingLogger<SmtpEmailSender>()).SendAsync(Mail, cts.Token));
    }

    [Theory]
    [InlineData("a@example.com\r\nBcc: victim@example.com")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("not-an-address")]
    [InlineData("")]
    public async Task AnythingButASingleMailboxIsRefused_SoTheAddressCannotInjectHeadersOrRecipients(string to)
    {
        using var server = new FakeSmtpServer();
        var log = new CapturingLogger<SmtpEmailSender>();
        await Sender(server.Port, log).SendAsync(Mail with { To = to });
        Assert.Empty(server.Messages);
    }

    [Fact]
    public async Task ASubjectCannotSmuggleHeaders()
    {
        using var server = new FakeSmtpServer();
        await Sender(server.Port, new CapturingLogger<SmtpEmailSender>()).SendAsync(Mail with { Subject = "Hi\r\nBcc: victim@example.com" });
        var mime = MimeMessage.Load(new MemoryStream(Assert.Single(server.Messages)));
        Assert.Empty(mime.Bcc);
        Assert.DoesNotContain(mime.Headers, h => h.Field.Equals("Bcc", StringComparison.OrdinalIgnoreCase));
    }

    // ---------------------------------------------------------------- configuration
    [Theory]
    [InlineData(null, "", 587, SmtpSecurity.StartTls, null, true)]                                  // not configured = log-only mode, always valid
    [InlineData("smtp.example.com", "no-reply@techbazar.example", 587, SmtpSecurity.StartTls, "user", true)]
    [InlineData("smtp.example.com", "no-reply@techbazar.example", 465, SmtpSecurity.SslOnConnect, "user", true)]
    [InlineData("localhost", "no-reply@techbazar.example", 1025, SmtpSecurity.None, null, true)]       // local catcher without credentials
    [InlineData("smtp.example.com", "no-reply@techbazar.example", 25, SmtpSecurity.None, "user", false)] // credentials in clear text: refused
    [InlineData("smtp.example.com", "", 587, SmtpSecurity.StartTls, null, false)]                       // no sender address
    [InlineData("smtp.example.com", "not-an-address", 587, SmtpSecurity.StartTls, null, false)]
    [InlineData("smtp.example.com", "no-reply@techbazar.example", 0, SmtpSecurity.StartTls, null, false)]
    [InlineData("smtp.example.com", "no-reply@techbazar.example", 70000, SmtpSecurity.StartTls, null, false)]
    public void OptionsValidation(string? host, string from, int port, SmtpSecurity security, string? user, bool valid) =>
        Assert.Equal(valid, SmtpOptions.IsValid(new SmtpOptions { Host = host, FromAddress = from, Port = port, Security = security, Username = user }));

    private static ServiceProvider Container(params (string, string?)[] settings)
    {
        var cfg = new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => KeyValuePair.Create(s.Item1, s.Item2))).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddInfrastructure(cfg);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void WithoutAHost_TheLogOnlySenderIsUsed_WithAHost_TheSmtpSenderIs()
    {
        using var none = Container(("Jwt:Key", new string('k', 40)), ("ConnectionStrings:DefaultConnection", "x"));
        Assert.IsType<LoggingEmailSender>(none.GetRequiredService<IEmailSender>());

        using var smtp = Container(("Jwt:Key", new string('k', 40)), ("ConnectionStrings:DefaultConnection", "x"),
            ("Email:Smtp:Host", "smtp.example.com"), ("Email:Smtp:FromAddress", "no-reply@techbazar.example"));
        Assert.IsType<SmtpEmailSender>(smtp.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void AnInsecureOrIncompleteSmtpConfigurationFailsFast()
    {
        using var bad = Container(("Jwt:Key", new string('k', 40)), ("ConnectionStrings:DefaultConnection", "x"),
            ("Email:Smtp:Host", "smtp.example.com"), ("Email:Smtp:FromAddress", "no-reply@techbazar.example"),
            ("Email:Smtp:Username", "u"), ("Email:Smtp:Security", "None"));
        Assert.Throws<OptionsValidationException>(() => bad.GetRequiredService<IOptions<SmtpOptions>>().Value);
    }
}
