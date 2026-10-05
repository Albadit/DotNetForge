using DotNetForge.Shared.Dtos;
using DotNetForge.Shared.Results;

namespace DotNetForge.Core.Installation;

/// <summary>
/// Orchestrates first-run installation (.docs/features/installation.md): install detection and the one-time
/// creation of the first Super Admin.
/// </summary>
public interface IInstallationService
{
    Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default);

    /// <summary>Validates the setup submission and, on success, creates the first Super Admin.</summary>
    Task<Result> InstallAsync(SetupRequest request, CancellationToken cancellationToken = default);
}
