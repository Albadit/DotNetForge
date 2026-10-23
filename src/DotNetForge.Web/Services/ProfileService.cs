using DotNetForge.Abstractions.Security;
using DotNetForge.Core.Validation;
using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Constants;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Services;

/// <summary>What the Profile screen's details form posts.</summary>
public sealed class ProfileInput
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
}

/// <summary>What the Profile screen's password form posts.</summary>
public sealed class PasswordChangeInput
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}

/// <summary>
/// The signed-in user's own account (.docs/pages/profile.md): name and email, and a password change that needs the
/// current password. Roles and status are not editable here (that is the Users screen). Both changes are audited
/// as <c>user.updated</c>.
/// </summary>
public sealed class ProfileService
{
    private readonly DotNetForgeDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditService _audit;

    public ProfileService(DotNetForgeDbContext db, IPasswordHasher hasher, IAuditService audit)
    {
        _db = db;
        _hasher = hasher;
        _audit = audit;
    }

    public async Task<string?> UpdateDetailsAsync(Guid userId, Guid tenantId, ProfileInput input, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);
        if (user is null)
        {
            return "Your account no longer exists.";
        }

        var email = input.Email?.Trim();
        if (!EmailValidator.IsValid(email) || email!.Length > UserManagementService.MaxEmailLength)
        {
            return $"Enter a valid email address (at most {UserManagementService.MaxEmailLength} characters).";
        }

        var first = Clean(input.FirstName);
        var last = Clean(input.LastName);
        if (first?.Length > UserManagementService.MaxNameLength || last?.Length > UserManagementService.MaxNameLength)
        {
            return $"First and last name are limited to {UserManagementService.MaxNameLength} characters each.";
        }

        // Case-insensitive, compared in memory so the check is the same on every database provider.
        var others = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Id != userId)
            .Select(u => u.Email)
            .ToListAsync(ct);
        if (others.Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            return $"Another user already uses {email}.";
        }

        user.FirstName = first;
        user.LastName = last;
        user.Email = email;
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UserUpdated, "User", user.Id.ToString(), user.Email,
            details: "Own profile", cancellationToken: ct);
        return null;
    }

    public async Task<string?> ChangePasswordAsync(Guid userId, Guid tenantId, PasswordChangeInput input, CancellationToken ct = default)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.TenantId == tenantId, ct);
        if (user is null)
        {
            return "Your account no longer exists.";
        }

        if (string.IsNullOrEmpty(input.CurrentPassword) || !_hasher.Verify(input.CurrentPassword, user.PasswordHash))
        {
            return "Your current password is not correct.";
        }

        if (PasswordPolicy.Validate(input.NewPassword) is { } weak)
        {
            return weak;
        }

        if (!string.Equals(input.NewPassword, input.ConfirmPassword, StringComparison.Ordinal))
        {
            return "The new password and its confirmation do not match.";
        }

        if (string.Equals(input.NewPassword, input.CurrentPassword, StringComparison.Ordinal))
        {
            return "Choose a password different from your current one.";
        }

        user.PasswordHash = _hasher.Hash(input.NewPassword!);
        await _db.SaveChangesAsync(ct);
        await _audit.LogAsync(AuditActions.UserUpdated, "User", user.Id.ToString(), user.Email,
            details: "Own password changed", cancellationToken: ct);
        return null;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
