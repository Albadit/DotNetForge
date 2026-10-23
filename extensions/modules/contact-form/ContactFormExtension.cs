using System.Net.Mail;
using System.Text.Json;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Sdk;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace ContactForm;

/// <summary>
/// Entry point of the Contact Form module. The form itself is <c>Views/Module.cshtml</c>, which the host renders on
/// the content pages listed in the "pages" setting; this class adds the endpoint the form posts to. A message is kept
/// as a JSON object in media storage (key prefix <c>ext/contact-form/</c>) and recorded in the audit log, where other
/// extensions (e.g. SMS Notifications) can react to it.
/// </summary>
public sealed class ContactFormExtension : IExtension
{
    public const string SubmitPath = "/ext/contact-form/submit";
    public const string AuditAction = "contactform.submitted";

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context) =>
        endpoints.MapPost(SubmitPath, (HttpContext http, IAntiforgery antiforgery, IFileStorage storage, IAuditService audit) =>
                SubmitAsync(http, antiforgery, storage, audit, context))
            .RequireRateLimiting(RateLimitPolicies.Forms);

    private static async Task<IResult> SubmitAsync(
        HttpContext http, IAntiforgery antiforgery, IFileStorage storage, IAuditService audit, ExtensionContext context)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(http);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.BadRequest("The form expired. Go back, reload the page and send it again.");
        }

        var form = await http.Request.ReadFormAsync(http.RequestAborted);
        var returnPath = LocalPath(form["returnPath"]);
        var name = form["name"].ToString().Trim();
        var email = form["email"].ToString().Trim();
        var message = form["message"].ToString().Trim();
        var maxLength = (int)Math.Clamp(context.GetInt64("maxMessageLength", 2000), 100, 10000);

        if (Validate(name, email, message, maxLength) is { } error)
        {
            return Results.Redirect($"{returnPath}?contact-error={Uri.EscapeDataString(error)}#contact-form");
        }

        // Media storage under the extension's own key prefix - never the (read-only) deployment folder.
        var now = DateTime.UtcNow;
        var key = $"ext/contact-form/{now:yyyy}/{now:MM}/{Guid.NewGuid():N}.json";
        var json = JsonSerializer.SerializeToUtf8Bytes(new { name, email, message, page = returnPath, receivedUtc = now });
        await storage.SaveAsync(key, new MemoryStream(json), "application/json", http.RequestAborted);
        await audit.LogAsync(AuditAction, "ContactMessage", key, $"{name} <{email}>",
            details: message.Length <= 200 ? message : message[..200] + "...", cancellationToken: http.RequestAborted);

        return Results.Redirect($"{returnPath}?contact-sent=1#contact-form");
    }

    private static string? Validate(string name, string email, string message, int maxLength)
    {
        if (name.Length is 0 or > 100)
        {
            return "Please enter your name (at most 100 characters).";
        }

        if (email.Length > 200 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
        {
            return "Please enter a valid email address.";
        }

        if (message.Length == 0 || message.Length > maxLength)
        {
            return $"Please enter a message of at most {maxLength} characters.";
        }

        return null;
    }

    /// <summary>
    /// The page to return to: a site-relative path of plain URL characters only, so the redirect can't leave the site
    /// (no <c>//host</c>, no backslashes, no scheme).
    /// </summary>
    private static string LocalPath(string? path) =>
        path is { Length: > 0 and <= 400 } p && p[0] == '/' && !p.StartsWith("//", StringComparison.Ordinal) &&
        p.All(c => char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_' or '.' or '~' or '%')
            ? p
            : "/";
}
