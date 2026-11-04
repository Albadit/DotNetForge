namespace DotNetForge.Web.Services.Localization.Keys;

/// <summary>
/// Texts shown through a variable on the Email Configuration and Email Templates screens (the names and triggers of the
/// shipped email templates, defined in <c>EmailTemplateCatalog</c> in Core), for the catalog check.
/// </summary>
internal static class SystemKeys
{
    public static readonly string[] All =
    {
        // EmailTemplateCatalog.All: names.
        AdminText.Key("Email confirmation"),
        AdminText.Key("Password reset"),
        AdminText.Key("Welcome email"),
        AdminText.Key("Account locked"),
        AdminText.Key("Sign-in code"),

        // EmailTemplateCatalog.All: triggers ("Sent when").
        AdminText.Key("A visitor registers while email confirmation is on, or asks for the link again."),
        AdminText.Key("Someone asks for a reset link on Forgot password."),
        AdminText.Key("A new account is ready: right after registering, or after confirming the email when confirmation is on."),
        AdminText.Key("Repeated wrong passwords lock an account for a while."),
        AdminText.Key("Two-factor authentication by email: someone signs in, or turns email codes on (.docs/features/mfa.md)."),
    };
}
