namespace DotNetForge.Core.Email;

/// <summary>A shipped email template: what triggers it, the variables it may use and its default text.</summary>
/// <param name="Key">Stable id, e.g. <c>password-reset</c> (the <c>EmailTemplates.Key</c> of a customized copy).</param>
/// <param name="Required">Variables the body must contain - without them the email is useless (e.g. the reset link).</param>
public sealed record EmailTemplateDefinition(
    string Key,
    string Name,
    string Trigger,
    IReadOnlyList<string> Variables,
    IReadOnlyList<string> Required,
    string DefaultSubject,
    string DefaultBody,
    IReadOnlyDictionary<string, string> SampleValues);

/// <summary>
/// The email templates the CMS sends (.docs/features/email.md#templates). Administrators may customize their subject
/// and body on Email → Templates; the defaults here apply while they don't. Invitation and admin-notification
/// templates are planned - no flow sends them yet.
/// </summary>
public static class EmailTemplateCatalog
{
    public const string EmailConfirmation = "email-confirmation";
    public const string PasswordReset = "password-reset";
    public const string WelcomeEmail = "welcome-email";
    public const string AccountLocked = "account-locked";

    public static readonly IReadOnlyList<EmailTemplateDefinition> All = new[]
    {
        new EmailTemplateDefinition(
            EmailConfirmation,
            "Email confirmation",
            "A visitor registers while email confirmation is on, or asks for the link again.",
            new[] { "userName", "confirmationUrl", "expiryDays", "appName", "appUrl" },
            new[] { "confirmationUrl" },
            "Confirm your email for {{appName}}",
            EmailLayout.Heading("Confirm your email") + "\n" +
            "<p style=\"margin:0 0 16px;\">Hello {{userName}}, welcome to {{appName}}. Confirm your email address to finish creating your account.</p>\n" +
            EmailLayout.Button("{{confirmationUrl}}", "Confirm my email") + "\n" +
            EmailLayout.LinkFallback("{{confirmationUrl}}") + "\n" +
            EmailLayout.Note("The link works for {{expiryDays}} days. If you didn't create an account, you can ignore this email."),
            Samples(("confirmationUrl", "https://cms.example.com/confirm-email?token=sample"), ("expiryDays", "2"))),
        new EmailTemplateDefinition(
            PasswordReset,
            "Password reset",
            "Someone asks for a reset link on Forgot password.",
            new[] { "userName", "resetUrl", "expiryMinutes", "appName", "appUrl" },
            new[] { "resetUrl" },
            "Reset your password for {{appName}}",
            EmailLayout.Heading("Reset your password") + "\n" +
            "<p style=\"margin:0 0 16px;\">Hello {{userName}}, someone asked to reset the password of your {{appName}} account.</p>\n" +
            EmailLayout.Button("{{resetUrl}}", "Choose a new password") + "\n" +
            EmailLayout.LinkFallback("{{resetUrl}}") + "\n" +
            EmailLayout.Note("The link works once and for {{expiryMinutes}} minutes. If you didn't ask for it, ignore this email - your password stays the same."),
            Samples(("resetUrl", "https://cms.example.com/reset-password?token=sample"), ("expiryMinutes", "60"))),
        new EmailTemplateDefinition(
            WelcomeEmail,
            "Welcome email",
            "A new account is ready: right after registering, or after confirming the email when confirmation is on.",
            new[] { "userName", "loginUrl", "appName", "appUrl" },
            Array.Empty<string>(),
            "Welcome to {{appName}}",
            EmailLayout.Heading("Welcome to {{appName}}") + "\n" +
            "<p style=\"margin:0 0 16px;\">Hello {{userName}}, your account is ready. Sign in any time with your email address and password.</p>\n" +
            EmailLayout.Button("{{loginUrl}}", "Sign in"),
            Samples(("loginUrl", "https://cms.example.com/login"))),
        new EmailTemplateDefinition(
            AccountLocked,
            "Account locked",
            "Repeated wrong passwords lock an account for a while.",
            new[] { "userName", "unlockUrl", "lockoutMinutes", "appName", "appUrl" },
            Array.Empty<string>(),
            "Your {{appName}} account is locked",
            EmailLayout.Heading("Your account is locked") + "\n" +
            "<p style=\"margin:0 0 16px;\">Hello {{userName}}, there were too many failed sign-ins to your {{appName}} account, so it is locked for {{lockoutMinutes}} minutes.</p>\n" +
            "<p style=\"margin:0 0 16px;\">If that wasn't you, reset your password - that also unlocks the account right away.</p>\n" +
            EmailLayout.Button("{{unlockUrl}}", "Reset my password") + "\n" +
            EmailLayout.Note("If it was you, wait {{lockoutMinutes}} minutes and sign in again."),
            Samples(("unlockUrl", "https://cms.example.com/forgot-password"), ("lockoutMinutes", "15"))),
    };

    public static EmailTemplateDefinition? Find(string? key) =>
        All.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.Ordinal));

    private static Dictionary<string, string> Samples(params (string Name, string Value)[] specific)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["userName"] = "Alex Example",
            ["appName"] = "DotNetForge CMS",
            ["appUrl"] = "https://cms.example.com",
        };
        foreach (var (name, value) in specific)
        {
            values[name] = value;
        }

        return values;
    }
}
