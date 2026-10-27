using System.Net;

namespace DotNetForge.Core.Email;

/// <summary>
/// The frame around every email body (.docs/features/email.md#templates): the site name on top, the body in a white
/// card with the forge-orange accent bar of the Forge design system, and a footer naming the site. Built from tables
/// with inline styles - what email clients render reliably (no stylesheets, no SVG logo). Not editable on
/// Email → Templates; the templates are its content.
/// </summary>
public static class EmailLayout
{
    // Forge design tokens (wwwroot/css/forge/tokens.css), inlined because email clients ignore stylesheets.
    public const string Charcoal = "#14171c";
    public const string Orange = "#f76707";
    public const string Text = "#16191e";
    public const string Muted = "#6c727d";
    public const string Border = "#e1e4e8";
    public const string Page = "#f7f8f9";
    public const string Font = "'Segoe UI', system-ui, -apple-system, BlinkMacSystemFont, Roboto, Helvetica, Arial, sans-serif";

    /// <summary>The finished HTML document: <paramref name="bodyHtml"/> (already rendered) inside the frame.</summary>
    /// <param name="banner">HTML put above the frame - only the admin preview uses it (to show the subject).</param>
    public static string Wrap(string bodyHtml, string appName, string appUrl, string? banner = null)
    {
        var name = WebUtility.HtmlEncode(appName);
        var url = WebUtility.HtmlEncode(appUrl.TrimEnd('/'));
        return $"""
            <!DOCTYPE html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="color-scheme" content="light"><title>{name}</title></head>
            <body style="margin:0;padding:0;background:{Page};">{banner}
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background:{Page};">
            <tr><td align="center" style="padding:32px 16px;">
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width:560px;">
            <tr><td style="padding:0 4px 16px;font-family:{Font};font-size:18px;font-weight:700;color:{Charcoal};">
            <span style="display:inline-block;width:10px;height:18px;margin-right:8px;vertical-align:-2px;background:{Orange};transform:skewX(-20deg);"></span>{name}
            </td></tr>
            <tr><td style="background:#ffffff;border:1px solid {Border};border-top:4px solid {Orange};border-radius:10px;padding:32px;font-family:{Font};font-size:15px;line-height:1.6;color:{Text};">
            {bodyHtml}
            </td></tr>
            <tr><td style="padding:16px 4px 0;font-family:{Font};font-size:12px;line-height:1.5;color:{Muted};">
            Sent by {name} &middot; <a href="{url}" style="color:{Muted};">{url}</a>
            </td></tr>
            </table>
            </td></tr>
            </table>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// A call-to-action link styled as the Forge primary button (orange fill, charcoal text) - for the default bodies.
    /// </summary>
    public static string Button(string hrefPlaceholder, string label) =>
        $"<p style=\"margin:24px 0;\"><a href=\"{hrefPlaceholder}\" style=\"display:inline-block;padding:12px 22px;" +
        $"background:{Orange};color:{Charcoal};font-weight:600;text-decoration:none;border-radius:6px;\">{label}</a></p>";

    /// <summary>The same link as text under the button, for clients that block buttons or images.</summary>
    public static string LinkFallback(string hrefPlaceholder) =>
        $"<p style=\"margin:0 0 16px;font-size:13px;color:{Muted};\">If the button doesn't work, open this link:<br>" +
        $"<a href=\"{hrefPlaceholder}\" style=\"color:#c2410c;word-break:break-all;\">{hrefPlaceholder}</a></p>";

    /// <summary>A heading for the default bodies.</summary>
    public static string Heading(string text) =>
        $"<h1 style=\"margin:0 0 16px;font-size:22px;line-height:1.3;color:{Charcoal};\">{text}</h1>";

    /// <summary>A small grey closing note for the default bodies.</summary>
    public static string Note(string text) =>
        $"<p style=\"margin:16px 0 0;font-size:13px;color:{Muted};\">{text}</p>";
}
