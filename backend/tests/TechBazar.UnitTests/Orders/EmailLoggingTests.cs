using Microsoft.Extensions.Logging;
using TechBazar.Application.Email;
using TechBazar.Infrastructure.Email;
using TechBazar.UnitTests.Support;

namespace TechBazar.UnitTests.Orders;

public class EmailLoggingTests
{
    private const string Body = "Hi Rahim Uddin, your order TB-1 ships to House 1, Road 2, Mirpur, Dhaka. Call 01712345678.";

    [Fact]
    public async Task InformationLogsContainNoPersonalDataFromTheEmailBody_OrTheFullAddress()
    {
        var log = new CapturingLogger<LoggingEmailSender>();
        await new LoggingEmailSender(log).SendAsync(new EmailMessage("rahim@example.com", "Order TB-1 confirmed", Body));

        var info = string.Join("\n", log.Entries.Where(e => e.Level >= LogLevel.Information).Select(e => e.Message));
        Assert.Contains("r***@example.com", info);
        Assert.Contains("Order TB-1 confirmed", info);
        Assert.DoesNotContain("rahim@example.com", info);
        Assert.DoesNotContain("01712345678", info);
        Assert.DoesNotContain("Mirpur", info);
        Assert.DoesNotContain("Rahim Uddin", info);
    }

    [Fact]
    public async Task TheFullBodyIsAvailableAtDebugLevelForDevelopment()
    {
        var log = new CapturingLogger<LoggingEmailSender>();
        await new LoggingEmailSender(log).SendAsync(new EmailMessage("rahim@example.com", "s", Body));
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Debug && e.Message.Contains(Body));
    }

    [Theory]
    [InlineData("rahim@example.com", "r***@example.com")]
    [InlineData("a@b.co", "a***@b.co")]
    [InlineData("not-an-email", "***")]
    [InlineData("@x.com", "***")]
    [InlineData("", "***")]
    public void MaskEmail(string input, string expected) => Assert.Equal(expected, LoggingEmailSender.MaskEmail(input));
}
