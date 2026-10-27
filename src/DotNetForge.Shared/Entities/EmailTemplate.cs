namespace DotNetForge.Shared.Entities;

/// <summary>
/// An administrator's version of one of the shipped email templates (.docs/pages/email-templates.md): a row exists
/// only while the template is customized - no row means the shipped default (<c>EmailTemplateCatalog</c>) is used,
/// and restoring the default deletes the row.
/// </summary>
public class EmailTemplate
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>The template's stable key from the catalog, e.g. <c>password-reset</c>. Unique.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Plain text with <c>{{variables}}</c>.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>HTML with <c>{{variables}}</c> (values are HTML-encoded when rendered).</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>UTC; also the edit form's concurrency check (a stale save is refused, never silently overwritten).</summary>
    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    public Guid? UpdatedById { get; set; }
}
