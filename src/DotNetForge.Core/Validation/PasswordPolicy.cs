namespace DotNetForge.Core.Validation;

/// <summary>
/// Password strength policy (.docs/features/security.md). Weak passwords must be rejected; the minimum length and
/// complexity are enforced here and applied by the setup wizard.
/// </summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    private static readonly HashSet<string> CommonWeakPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "12345678", "123456789", "qwertyui", "qwerty123",
        "letmein1", "changeme", "admin123", "welcome1", "iloveyou", "11111111",
    };

    /// <summary>Validates a password and returns an error message when it is too weak, else null.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "Password is required.";
        }

        if (password.Length < MinLength)
        {
            return $"Password must be at least {MinLength} characters long.";
        }

        if (!password.Any(char.IsLetter))
        {
            return "Password must contain at least one letter.";
        }

        if (!password.Any(char.IsDigit))
        {
            return "Password must contain at least one number.";
        }

        if (CommonWeakPasswords.Contains(password))
        {
            return "Password is too common. Choose a stronger password.";
        }

        return null;
    }

    public static bool IsValid(string? password) => Validate(password) is null;
}
