using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Results;

namespace DotNetForge.Shared.Stores;

/// <summary>
/// Persistence operations behind the first-run installation flow. Implemented by the Data layer so
/// the Core <c>InstallationService</c> can orchestrate setup without depending on EF Core directly
/// (keeps the Core/Data dependency direction intact - see ARCHITECTURE.md).
/// </summary>
public interface IInstallationStore
{
    /// <summary>True once the CMS has completed first-run setup.</summary>
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically creates the first admin (with a pre-hashed password), assigns the Super Admin
    /// role, and sets the installed flag. The first writer wins under concurrency; later callers
    /// receive a failure result indicating the CMS is already installed.
    /// </summary>
    Task<Result> InstallFirstAdminAsync(User admin, CancellationToken cancellationToken = default);
}
