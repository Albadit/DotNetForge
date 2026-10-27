using DotNetForge.Core.Email;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>A template as the editor and the senders see it: the customized text, else the shipped default.</summary>
/// <param name="Version">The customized row's <c>UpdatedDate</c> ticks (0 = the default); a save must post it back.</param>
public sealed record EmailTemplateState(EmailTemplateDefinition Definition, string Subject, string Body, bool IsCustomized, long Version, DateTime? UpdatedDate);

/// <summary>
/// Email → Templates (.docs/pages/email-templates.md): customized subjects and bodies of the
/// <see cref="EmailTemplateCatalog"/> templates, stored in <c>EmailTemplates</c> (a row only while customized), and
/// the rendering every sender uses. Validation and placeholder filling are <see cref="EmailTemplateRenderer"/>'s.
/// </summary>
public sealed class EmailTemplateService
{
    private readonly DotNetForgeDbContext _db;
    private readonly IAuditService _audit;
    private readonly ILogger<EmailTemplateService> _logger;

    public EmailTemplateService(DotNetForgeDbContext db, IAuditService audit, ILogger<EmailTemplateService> logger)
    {
        _db = db;
        _audit = audit;
        _logger = logger;
    }

    public async Task<IReadOnlyList<EmailTemplateState>> ListAsync(CancellationToken ct = default)
    {
        var rows = await _db.EmailTemplates.AsNoTracking().ToListAsync(ct);
        return EmailTemplateCatalog.All.Select(d => State(d, rows.FirstOrDefault(r => r.Key == d.Key))).ToList();
    }

    public async Task<EmailTemplateState?> GetAsync(string key, CancellationToken ct = default)
    {
        if (EmailTemplateCatalog.Find(key) is not { } definition)
        {
            return null;
        }

        return State(definition, await _db.EmailTemplates.AsNoTracking().FirstOrDefaultAsync(t => t.Key == definition.Key, ct));
    }

    /// <summary>
    /// Saves a customized subject and body; returns the errors (nothing is saved when there are any). Saving text equal
    /// to the default makes the template default again. A save based on an older <paramref name="version"/> is refused,
    /// so two editors never silently overwrite each other.
    /// </summary>
    public async Task<List<string>> SaveAsync(string key, string? subject, string? body, long version, Guid? userId, CancellationToken ct = default)
    {
        var definition = EmailTemplateCatalog.Find(key) ?? throw new ArgumentException($"Unknown email template '{key}'.", nameof(key));
        subject = subject?.Trim();
        body = Normalize(body);
        var errors = EmailTemplateRenderer.Validate(definition, subject, body);
        if (errors.Count > 0)
        {
            return errors;
        }

        var row = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.Key == definition.Key, ct);
        if ((row?.UpdatedDate.Ticks ?? 0) != version)
        {
            return new List<string> { "Someone else changed this template since you opened it. Reload to see their version, then make your change again." };
        }

        var isDefault = subject == definition.DefaultSubject && body == Normalize(definition.DefaultBody);
        if (isDefault)
        {
            if (row is not null)
            {
                _db.EmailTemplates.Remove(row);
            }
        }
        else
        {
            if (row is null)
            {
                row = new EmailTemplate { Key = definition.Key };
                _db.EmailTemplates.Add(row);
            }

            row.Subject = subject!;
            row.Body = body!;
            row.UpdatedDate = DateTime.UtcNow;
            row.UpdatedById = userId;
        }

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two first saves at once collide on the unique key.
            return new List<string> { "Someone else saved this template at the same time. Reload and try again." };
        }

        await _audit.LogAsync(AuditActions.SettingsChanged, "EmailTemplate", definition.Key, $"Email template: {definition.Name}",
            details: isDefault ? "Saved as the default" : "Customized", cancellationToken: ct);
        return errors;
    }

    /// <summary>Back to the shipped default; false (and no audit entry) when it wasn't customized.</summary>
    public async Task<bool> RestoreAsync(string key, CancellationToken ct = default)
    {
        var definition = EmailTemplateCatalog.Find(key) ?? throw new ArgumentException($"Unknown email template '{key}'.", nameof(key));
        var row = await _db.EmailTemplates.FirstOrDefaultAsync(t => t.Key == definition.Key, ct);
        if (row is null)
        {
            return false;
        }

        _db.EmailTemplates.Remove(row);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.SettingsChanged, "EmailTemplate", definition.Key, $"Email template: {definition.Name}",
            details: "Default restored", cancellationToken: ct);
        return true;
    }

    /// <summary>
    /// The subject and the finished HTML document to send: the customized template or the default, inside
    /// <see cref="EmailLayout"/>. <paramref name="values"/> must include <c>appName</c> and <c>appUrl</c>.
    /// </summary>
    public async Task<(string Subject, string Body)> RenderAsync(string key, IReadOnlyDictionary<string, string> values, CancellationToken ct = default)
    {
        var state = await GetAsync(key, ct) ?? throw new ArgumentException($"Unknown email template '{key}'.", nameof(key));
        var (subject, body) = (state.Subject, state.Body);
        if (state.IsCustomized && EmailTemplateRenderer.Validate(state.Definition, subject, body).Count > 0)
        {
            // Only possible if the catalog's variables changed after the template was saved: never send a broken one.
            LogInvalidCustomTemplate(state.Definition.Key);
            (subject, body) = (state.Definition.DefaultSubject, state.Definition.DefaultBody);
        }

        return (EmailTemplateRenderer.RenderSubject(subject, values),
            EmailLayout.Wrap(EmailTemplateRenderer.RenderBody(body, values), values["appName"], values["appUrl"]));
    }

    /// <summary>Textareas post CRLF; templates are stored with LF.</summary>
    public static string? Normalize(string? body) => body?.ReplaceLineEndings("\n").Trim();

    private static EmailTemplateState State(EmailTemplateDefinition definition, EmailTemplate? row) => row is null
        ? new EmailTemplateState(definition, definition.DefaultSubject, definition.DefaultBody, false, 0, null)
        : new EmailTemplateState(definition, row.Subject, row.Body, true, row.UpdatedDate.Ticks, row.UpdatedDate);

    private void LogInvalidCustomTemplate(string key) =>
        _logger.LogWarning("The customized email template {TemplateKey} no longer validates; the default was sent instead.", key);
}
