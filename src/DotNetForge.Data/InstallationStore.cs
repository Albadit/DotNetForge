using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Results;
using DotNetForge.Shared.Stores;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// EF Core implementation of <see cref="IInstallationStore"/>. Creating the first admin and setting
/// the installed flag happen inside one transaction so two concurrent setup submissions can never
/// both create a Super Admin - the first to commit wins (installation_setup.md).
/// </summary>
public sealed class InstallationStore : IInstallationStore
{
    private readonly DotNetForgeDbContext _db;

    public InstallationStore(DotNetForgeDbContext db)
    {
        _db = db;
    }

    public async Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default)
    {
        var state = await _db.SystemState.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return state?.Installed ?? false;
    }

    public async Task<Result> InstallFirstAdminAsync(User admin, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(admin);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var state = await _db.SystemState.FirstOrDefaultAsync(cancellationToken);
        if (state is null)
        {
            state = new SystemState { Id = 1 };
            _db.SystemState.Add(state);
        }

        if (state.Installed)
        {
            return Result.Fail("The CMS is already installed.");
        }

        var tenant = await _db.Tenants.OrderBy(t => t.CreatedDate).FirstOrDefaultAsync(cancellationToken);
        if (tenant is null)
        {
            return Result.Fail("The default tenant has not been seeded.");
        }

        var superAdminRole = await _db.Roles
            .FirstOrDefaultAsync(r => r.TenantId == tenant.Id && r.Name == Roles.SuperAdmin, cancellationToken);
        if (superAdminRole is null)
        {
            return Result.Fail("The Super Admin role has not been seeded.");
        }

        admin.TenantId = tenant.Id;
        _db.Users.Add(admin);
        _db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = superAdminRole.Id });

        state.Installed = true;
        state.InstalledAtUtc = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
            return Result.Ok();
        }
        catch (DbUpdateException)
        {
            await tx.RollbackAsync(cancellationToken);
            return Result.Fail("The CMS is already installed.");
        }
    }
}
