using System.Globalization;
using System.Text.RegularExpressions;

namespace DotNetForge.Core.Validation;

/// <summary>
/// Locale codes (.docs/features/internationalization.md#rules-and-validation): a well-formed BCP-47 tag - language,
/// optional script, optional region (<c>en</c>, <c>en-US</c>, <c>zh-Hans</c>, <c>sr-Latn-RS</c>, <c>es-419</c>) - that
/// the culture data knows, in its canonical case. Pure code, no I/O.
/// </summary>
public static partial class LocaleCodes
{
    /// <summary>The longest tag stored (the <c>Locales.Code</c> column).</summary>
    public const int MaxLength = 35;

    /// <summary>
    /// Validates <paramref name="input"/> and returns its canonical form (<c>de-de</c> → <c>de-DE</c>), or null with
    /// <paramref name="error"/> set.
    /// </summary>
    public static string? Normalize(string? input, out string? error)
    {
        var tag = input?.Trim().Replace('_', '-') ?? string.Empty;
        if (tag.Length == 0)
        {
            error = "Enter a locale code, such as en, de-DE or pt-BR.";
            return null;
        }

        if (tag.Length > MaxLength || !Tag().IsMatch(tag))
        {
            error = $"\"{tag}\" is not a valid locale code. Use a language code, optionally with script and region: en, de-DE, zh-Hans, pt-BR.";
            return null;
        }

        var culture = Find(tag);
        if (culture is null)
        {
            error = $"\"{tag}\" is not a known language. Use a code such as en, de-DE or pt-BR.";
            return null;
        }

        error = null;
        return Canonical(tag);
    }

    /// <summary>A display name for a valid code: the language's own name ("Deutsch (Deutschland)"), else the code.</summary>
    public static string SuggestedName(string code)
    {
        var culture = Find(code);
        if (culture is null)
        {
            return code;
        }

        var name = culture.NativeName;
        return name.Length > 0 ? char.ToUpper(name[0], culture) + name[1..] : code;
    }

    /// <summary>The language part of a code (<c>de-DE</c> → <c>de</c>).</summary>
    public static string Language(string code) => code.Split('-')[0].ToLowerInvariant();

    private static CultureInfo? Find(string tag)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag, predefinedOnly: true);
            return culture.Name.Length == 0 ? null : culture;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    // language lower case, script title case, region upper case (or three digits): the BCP-47 conventions.
    private static string Canonical(string tag)
    {
        var parts = tag.Split('-');
        for (var i = 0; i < parts.Length; i++)
        {
            parts[i] = i == 0 ? parts[i].ToLowerInvariant()
                : parts[i].Length == 4 ? char.ToUpperInvariant(parts[i][0]) + parts[i][1..].ToLowerInvariant()
                : parts[i].ToUpperInvariant();
        }

        return string.Join('-', parts);
    }

    [GeneratedRegex("^[A-Za-z]{2,3}(-[A-Za-z]{4})?(-([A-Za-z]{2}|[0-9]{3}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Tag();
}
