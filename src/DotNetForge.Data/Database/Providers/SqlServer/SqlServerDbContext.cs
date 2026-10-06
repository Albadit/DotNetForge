using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data.Database.Providers.SqlServer;

/// <summary>
/// The CMS model as a distinct type so SQL Server gets its own migration set (<c>Migrations/SqlServer/</c>), like
/// <see cref="PostgreSqlDbContext"/>. Registered as <see cref="DotNetForgeDbContext"/>; application code never refers
/// to this class (.docs/database/providers.md).
/// </summary>
public sealed class SqlServerDbContext : DotNetForgeDbContext
{
    public SqlServerDbContext(DbContextOptions<SqlServerDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        // The single SystemState row always has Id 1. SQL Server would make an int key an IDENTITY column, which
        // rejects explicit values (the other databases accept them).
        b.Entity<SystemState>().Property(s => s.Id).ValueGeneratedNever();
    }
}
