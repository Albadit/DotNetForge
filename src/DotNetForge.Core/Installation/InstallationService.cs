using DotNetForge.Abstractions.Security;
using DotNetForge.Core.Validation;
using DotNetForge.Shared.Dtos;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Shared.Results;
using DotNetForge.Shared.Stores;

namespace DotNetForge.Core.Installation;

/// <summary>
/// Default installation orchestrator. Validates the setup submission (email format, password
/// policy, confirm match) and delegates atomic persistence to the <see cref="IInstallationStore"/>.
/// The first writer to commit wins under concurrent submissions; later callers are told the CMS is
/// already installed (.docs/features/installation.md).
/// </summary>
public sealed class InstallationService : IInstallationService
{
    // Column limits of User (DotNetForgeDbContext), checked before saving.
    private const int MaxEmailLength = 256;
    private const int MaxNameLength = 100;

    private readonly IInstallationStore _store;
    private readonly IPasswordHasher _passwordHasher;

    public InstallationService(IInstallationStore store, IPasswordHasher passwordHasher)
    {
        _store = store;
        _passwordHasher = passwordHasher;
    }

    public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) =>
        _store.IsInstalledAsync(cancellationToken);

    public async Task<Result> InstallAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (await _store.IsInstalledAsync(cancellationToken))
        {
            return Result.Fail("The CMS is already installed.");
        }

        var email = request.Email?.Trim() ?? string.Empty;
        if (!EmailValidator.IsValid(email) || email.Length > MaxEmailLength)
        {
            return Result.Fail("A valid email address is required.");
        }

        if (request.FirstName?.Trim().Length > MaxNameLength || request.LastName?.Trim().Length > MaxNameLength)
        {
            return Result.Fail($"First and last name must be at most {MaxNameLength} characters.");
        }

        if (PasswordPolicy.Validate(request.Password) is { } passwordError)
        {
            return Result.Fail(passwordError);
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return Result.Fail("Password and confirmation do not match.");
        }

        var admin = new User
        {
            FirstName = string.IsNullOrWhiteSpace(request.FirstName) ? null : request.FirstName.Trim(),
            LastName = string.IsNullOrWhiteSpace(request.LastName) ? null : request.LastName.Trim(),
            Email = email,
            PasswordHash = _passwordHasher.Hash(request.Password),
            Status = UserStatus.Enabled,
            EmailConfirmed = true,
            CreatedDate = DateTime.UtcNow,
        };

        return await _store.InstallFirstAdminAsync(admin, cancellationToken);
    }
}
