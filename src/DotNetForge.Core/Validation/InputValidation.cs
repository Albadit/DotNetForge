using System.Text;
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

/// <summary>
/// Produces and validates URL-safe slugs (content_manager.md): lowercase letters, digits, and
/// single hyphens only.
/// </summary>
public static partial class SlugHelper
{
    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();

    public static bool IsValid(string? slug) =>
        !string.IsNullOrWhiteSpace(slug) && SlugPattern().IsMatch(slug);

    /// <summary>Converts arbitrary text into a valid slug.</summary>
    public static string Slugify(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        var lastWasHyphen = false;

        foreach (var ch in text.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) && ch < 128)
            {
                sb.Append(ch);
                lastWasHyphen = false;
            }
            else if (!lastWasHyphen && sb.Length > 0)
            {
                sb.Append('-');
                lastWasHyphen = true;
            }
        }

        return sb.ToString().Trim('-');
    }
}
