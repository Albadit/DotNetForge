using DotNetForge.Data;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data.Database.Providers.MySql;

/// <summary>
/// The CMS model as a distinct type so MySQL gets its own migration set (<c>Migrations/MySql/</c>), like
/// <see cref="PostgreSqlDbContext"/>. Registered as <see cref="DotNetForgeDbContext"/>; application code never refers
/// to this class (.docs/database/providers.md).
/// </summary>
public sealed class MySqlDbContext : DotNetForgeDbContext
{
    public MySqlDbContext(DbContextOptions<MySqlDbContext> options) : base(options) { }
}
