using DotNetForge.Abstractions.Database;
using DotNetForge.Data.Database;
using DotNetForge.Shared.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Wrong credentials against the real test servers: each provider reports a <see cref="DatabaseAuthenticationException"/>.
/// Each test is skipped (and reported as skipped) unless its server's <c>DNF_TEST_*</c> variable is set.
/// </summary>
public sealed class DatabaseCredentialTests
{
    [DatabaseServerFact("DNF_TEST_POSTGRES")]
    public Task PostgreSql_reports_wrong_credentials() =>
        AssertAuthenticationErrorAsync("postgresql", Server("DNF_TEST_POSTGRES") + ";Password=wrong-password;Database=postgres");

    [DatabaseServerFact("DNF_TEST_SQLSERVER")]
    public Task SqlServer_reports_wrong_credentials() =>
        AssertAuthenticationErrorAsync("sqlserver", Server("DNF_TEST_SQLSERVER") + ";Password=wrong-password;Initial Catalog=master");

    [DatabaseServerFact("DNF_TEST_MYSQL")]
    public Task MySql_reports_wrong_credentials() =>
        AssertAuthenticationErrorAsync("mysql", Server("DNF_TEST_MYSQL") + ";Pwd=wrong-password");

    private static string Server(string variable) => Environment.GetEnvironmentVariable(variable)!.TrimEnd(';');

    /// <summary>Later keys win in ADO.NET connection strings, so the appended password replaces the real one.</summary>
    private static async Task AssertAuthenticationErrorAsync(string provider, string connectionString)
    {
        var services = new ServiceCollection().AddLogging().AddDefaultDatabaseProviders();
        services.AddDotNetForgeDatabases(
            new DatabaseSettings { Provider = provider, ConnectionString = connectionString },
            Array.Empty<DatabaseSettings>(),
            new DatabaseHostContext(IsDevelopment: true, DevelopmentDataRoot: Path.GetTempPath()));

        var result = await services.BuildServiceProvider().GetRequiredService<IDatabaseService>().TestConnectionAsync();

        Assert.IsType<DatabaseAuthenticationException>(result.Error);
        Assert.DoesNotContain("wrong-password", result.Error!.Message, StringComparison.Ordinal);
    }
}

/// <summary>A fact that runs only when a database server is configured for the tests (like <c>[S3Fact]</c>).</summary>
public sealed class DatabaseServerFactAttribute : FactAttribute
{
    public DatabaseServerFactAttribute(string variable)
    {
        if (Environment.GetEnvironmentVariable(variable) is not { Length: > 0 })
        {
            Skip = $"Set {variable} to run against a real server (.docs/guides/testing.md#databases).";
        }
    }
}
