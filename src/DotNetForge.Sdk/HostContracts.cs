namespace DotNetForge.Sdk;

/// <summary>
/// Which extensions are enabled on the Plugins screen. The host already gates everything it calls; inject this when an
/// extension runs code on its own (a hosted service, a timer) so it stays quiet while disabled.
/// </summary>
public interface IExtensionState
{
    ValueTask<bool> IsEnabledAsync(string extensionId, CancellationToken cancellationToken = default);

    /// <summary>The ids of every enabled extension (case-insensitive set).</summary>
    ValueTask<IReadOnlySet<string>> GetEnabledAsync(CancellationToken cancellationToken = default);
}

/// <summary>An audit entry the CMS saved, as passed to <see cref="IAuditEventHandler"/>.</summary>
/// <param name="Action">The audit action, e.g. <c>user.login.failed</c> (<c>AuditActions</c>).</param>
/// <param name="UserDisplay">Who acted (display name, email or API token), if known.</param>
public sealed record AuditEvent(
    string Action,
    string? EntityType,
    string? EntityId,
    string? EntityDisplay,
    string? Details,
    bool Success,
    Guid? TenantId,
    string? UserDisplay,
    string IpAddress,
    DateTime TimestampUtc);

/// <summary>Receives audit events (see <c>AddAuditEventHandler</c>). Runs in the background in its own DI scope.</summary>
public interface IAuditEventHandler
{
    Task HandleAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

/// <summary>Names an authentication extension needs to plug into the host's sign-in (see <c>AddLoginProvider</c>).</summary>
public static class ExternalLogin
{
    /// <summary>
    /// The temporary cookie scheme the host registers for external sign-in. Set it as the <c>SignInScheme</c> of the
    /// extension's remote handler; after the handler's own callback the host reads this cookie, signs the matching CMS
    /// user in with its normal cookie and deletes it.
    /// </summary>
    public const string SignInScheme = "dnf.external";
}
