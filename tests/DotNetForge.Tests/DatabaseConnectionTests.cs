using DotNetForge.Abstractions.Database;
using DotNetForge.Data.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Real drivers against an address where nothing listens: every provider must report a
/// <see cref="DatabaseConnectionException"/> (not a driver exception, not a hang) and never leak the connection string.
/// </summary>
public sealed class DatabaseConnectionTests
{
    [Theory]
    [InlineData("postgresql", "Host=127.0.0.1;Port=1;Database=x;Username=u;Password=s3cret;Timeout=3")]
    [InlineData("sqlserver", "Server=127.0.0.1,1;Initial Catalog=x;User Id=u;Password=s3cret;Connect Timeout=3;TrustServerCertificate=true")]
    [InlineData("mysql", "Server=127.0.0.1;Port=1;Database=x;Uid=u;Pwd=s3cret;Connection Timeout=3")]
    [InlineData("mongodb", "mongodb://u:s3cret@127.0.0.1:1/x?serverSelectionTimeoutMS=2000&connectTimeoutMS=2000")]
    public async Task Unreachable_servers_are_connection_errors(string provider, string connectionString)
    {
        var services = new ServiceCollection().AddLogging().AddDefaultDatabaseProviders();
        services.AddDotNetForgeDatabases(
            new DatabaseSettings { Provider = provider, ConnectionString = connectionString },
            Array.Empty<DatabaseSettings>(),
            new DatabaseHostContext(IsDevelopment: true, DevelopmentDataRoot: Path.GetTempPath()));
        var db = services.BuildServiceProvider().GetRequiredService<IDatabaseService>();

        var result = await db.TestConnectionAsync();

        Assert.False(result.Success);
        Assert.IsType<DatabaseConnectionException>(result.Error);
        Assert.Equal("main", result.Error!.Database);
        Assert.DoesNotContain("s3cret", result.Error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret", result.Data?.Server ?? string.Empty, StringComparison.Ordinal);
    }
}
