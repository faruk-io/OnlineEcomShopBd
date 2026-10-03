using System.Net;

namespace TechBazar.Application.Email;

/// <summary>Account-security emails. Links come from configuration; all interpolated values are HTML-encoded.</summary>
public static class AccountEmails
{
    public static EmailMessage VerifyEmail(string to, string fullName, string link, int validHours) => new(to,
        "Verify your email - TechBazar BD",
        $"Hi {fullName},\n\nConfirm your email address to finish setting up your TechBazar BD account:\n{link}\n\n" +
        $"The link works once and expires in {validHours} hours. If you did not create an account, you can ignore this email.",
        $"<p>Hi {H(fullName)},</p><p>Confirm your email address to finish setting up your TechBazar BD account:</p>" +
        $"<p><a href=\"{H(link)}\">Verify my email</a></p><p>The link works once and expires in {validHours} hours. If you did not create an account, you can ignore this email.</p>");

    public static EmailMessage PasswordReset(string to, string fullName, string link, int validMinutes) => new(to,
        "Reset your password - TechBazar BD",
        $"Hi {fullName},\n\nSomeone (hopefully you) asked to reset the password of your TechBazar BD account:\n{link}\n\n" +
        $"The link works once and expires in {validMinutes} minutes. If you did not ask for this, ignore this email: your password stays unchanged.",
        $"<p>Hi {H(fullName)},</p><p>Someone (hopefully you) asked to reset the password of your TechBazar BD account.</p>" +
        $"<p><a href=\"{H(link)}\">Choose a new password</a></p><p>The link works once and expires in {validMinutes} minutes. " +
        "If you did not ask for this, ignore this email: your password stays unchanged.</p>");

    public static EmailMessage PasswordChanged(string to, string fullName, string forgotPasswordLink) => new(to,
        "Your password was changed - TechBazar BD",
        $"Hi {fullName},\n\nThe password of your TechBazar BD account was just changed and you were signed out on all devices.\n" +
        $"If this was not you, reset it immediately: {forgotPasswordLink} and contact support.",
        $"<p>Hi {H(fullName)},</p><p>The password of your TechBazar BD account was just changed and you were signed out on all devices.</p>" +
        $"<p>If this was not you, <a href=\"{H(forgotPasswordLink)}\">reset it immediately</a> and contact support.</p>");

    public static EmailMessage MfaChanged(string to, string fullName, string what, string forgotPasswordLink) => new(to,
        "Two-step verification changed - TechBazar BD",
        $"Hi {fullName},\n\n{what}\nIf this was not you, reset your password immediately: {forgotPasswordLink} and contact support.",
        $"<p>Hi {H(fullName)},</p><p>{H(what)}</p><p>If this was not you, <a href=\"{H(forgotPasswordLink)}\">reset your password immediately</a> and contact support.</p>");

    private static string H(string s) => WebUtility.HtmlEncode(s);
}
