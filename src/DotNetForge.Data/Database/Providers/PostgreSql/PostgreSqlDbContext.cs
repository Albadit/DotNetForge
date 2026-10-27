using DotNetForge.Data;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data.Database.Providers.PostgreSql;

/// <summary>
/// The CMS model on PostgreSQL - the CMS database - with its migrations in <c>Database/Providers/PostgreSql/Migrations/</c>
/// (EF Core binds migrations to a context type). The provider registers it as <see cref="DotNetForgeDbContext"/>, so
/// application code never refers to this class.
/// </summary>
public sealed class PostgreSqlDbContext : DotNetForgeDbContext
{
    public PostgreSqlDbContext(DbContextOptions<PostgreSqlDbContext> options) : base(options) { }
}
