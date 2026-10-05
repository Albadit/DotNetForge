using System.Text.RegularExpressions;

namespace DotNetForge.Core.Validation;

/// <summary>Email format validation (server-side, regardless of any client-side checks).</summary>
public static partial class EmailValidator
{
    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailPattern();

    public static bool IsValid(string? email) =>
        !string.IsNullOrWhiteSpace(email) && EmailPattern().IsMatch(email.Trim());
}
