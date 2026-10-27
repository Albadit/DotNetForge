namespace DotNetForge.Shared.Configuration;

/// <summary>
/// The SMTP settings in effect (.docs/features/email.md): the <c>SMTP_*</c> values from <c>.env</c>
/// (<see cref="AppEnvironment.Email"/>), shown read-only on Email → Configuration. Read per send.
/// </summary>
public interface IEmailSettingsSource
{
    Task<EmailSettings> GetAsync(CancellationToken cancellationToken = default);
}
