using DotNetForge.Data;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data.Database.Providers.PostgreSql;

/// <summary>
/// The same model as <see cref="DotNetForgeDbContext"/>, as a distinct type so PostgreSQL gets its own migration set
/// (<c>Migrations/PostgreSql/</c>) with PostgreSQL column types. EF Core binds migrations to a context type, so one
/// type per provider is the simplest way to keep each SQL provider on real migrations. The provider registers it as
/// <see cref="DotNetForgeDbContext"/>, so application code never refers to this class.
/// </summary>
public sealed class PostgreSqlDbContext : DotNetForgeDbContext
{
    public PostgreSqlDbContext(DbContextOptions<PostgreSqlDbContext> options) : base(options) { }
}
