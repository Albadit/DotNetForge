using System.Net;
using System.Text.RegularExpressions;

namespace DotNetForge.Core.Email;

/// <summary>
/// Checks and fills <c>{{variable}}</c> placeholders of email templates (.docs/features/email.md#templates). Pure
/// logic: a template is only saved, previewed or sent when <see cref="Validate"/> finds nothing, so an unresolved
/// placeholder never reaches a recipient.
/// </summary>
public static partial class EmailTemplateRenderer
{
    public const int MaxSubjectLength = 500;
    public const int MaxBodyLength = 20000;

    /// <summary>The problems of a subject/body pair for <paramref name="template"/>; empty when it can be used.</summary>
    public static List<string> Validate(EmailTemplateDefinition template, string? subject, string? body)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(subject))
        {
            errors.Add("Enter a subject.");
        }
        else if (subject.Length > MaxSubjectLength)
        {
            errors.Add($"The subject is limited to {MaxSubjectLength} characters.");
        }
        else if (subject.Contains('\n') || subject.Contains('\r'))
        {
            errors.Add("The subject must be one line.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            errors.Add("Enter a body.");
        }
        else if (body.Length > MaxBodyLength)
        {
            errors.Add($"The body is limited to {MaxBodyLength} characters.");
        }

        foreach (var (part, text) in new[] { ("subject", subject), ("body", body) })
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            foreach (var name in Variables(text).Distinct(StringComparer.Ordinal))
            {
                if (!template.Variables.Contains(name, StringComparer.Ordinal))
                {
                    errors.Add($"Unknown variable {{{{{name}}}}} in the {part}. Available: {string.Join(", ", template.Variables.Select(v => "{{" + v + "}}"))}.");
                }
            }

            // Braces left after removing the valid placeholders: a typo such as "{{resetUrl}" would reach the reader.
            var rest = Placeholder().Replace(text, string.Empty);
            if (rest.Contains("{{", StringComparison.Ordinal) || rest.Contains("}}", StringComparison.Ordinal))
            {
                errors.Add($"The {part} has an incomplete placeholder: write variables as {{{{name}}}}.");
            }
        }

        if (!string.IsNullOrEmpty(body))
        {
            foreach (var required in template.Required.Where(r => !Variables(body).Contains(r, StringComparer.Ordinal)))
            {
                errors.Add($"The body must contain {{{{{required}}}}} - without it the email is useless.");
            }
        }

        return errors;
    }

    /// <summary>The variable names used in <paramref name="text"/>, in order.</summary>
    public static IEnumerable<string> Variables(string text) =>
        Placeholder().Matches(text).Select(m => m.Groups[1].Value);

    /// <summary>The subject with the values as plain text (a mail header is not HTML).</summary>
    public static string RenderSubject(string subject, IReadOnlyDictionary<string, string> values) =>
        Fill(subject, values, encode: false).ReplaceLineEndings(" ");

    /// <summary>The body with the values HTML-encoded (a user's name can't inject markup).</summary>
    public static string RenderBody(string body, IReadOnlyDictionary<string, string> values) => Fill(body, values, encode: true);

    private static string Fill(string text, IReadOnlyDictionary<string, string> values, bool encode) =>
        Placeholder().Replace(text, m => values.TryGetValue(m.Groups[1].Value, out var value)
            ? encode ? WebUtility.HtmlEncode(value) : value
            : throw new InvalidOperationException($"No value for {{{{{m.Groups[1].Value}}}}}."));

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9]*)\s*\}\}")]
    private static partial Regex Placeholder();
}
