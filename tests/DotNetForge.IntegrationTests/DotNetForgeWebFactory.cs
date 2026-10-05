using System.Net;
using System.Text.RegularExpressions;
using DotNetForge.Abstractions.Security;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Data;
using DotNetForge.Infrastructure.Storage;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Stores;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Boots the real DotNetForge.Web host against an isolated, throwaway database and a throwaway storage directory,
/// so each test gets a fresh, fully-migrated CMS and nothing is written inside the repository. Configuration is
/// supplied through environment variables (read by the host's .env loader) before the host builds.
/// </summary>
/// <remarks>
/// Media storage: the host requires S3 outside Development, so the factory supplies placeholder S3 settings and
/// replaces <see cref="IFileStorage"/> with a <see cref="LocalFileStorage"/> in <see cref="StoragePath"/>. Tests
/// therefore need no S3 server; the S3 implementation itself is covered by the live contract tests
/// (<c>DNF_TEST_S3_*</c>, .docs/guides/testing.md).
/// </remarks>
/// <remarks>
/// SQLite by default. Set <c>DNF_TEST_POSTGRES</c> to a server connection string without a database (e.g.
/// <c>Host=localhost;Port=5432;Username=postgres;Password=postgres</c>) to run the same tests against PostgreSQL;
/// each factory then creates and drops its own database.
/// </remarks>
public sealed partial class DotNetForgeWebFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@example.com";
    public const string AdminPassword = "Sup3rSecret";

    private readonly string _workDir;
    private readonly string _environment;

    public DotNetForgeWebFactory(string environment = "Development")
    {
        _environment = environment;
        _workDir = Path.Combine(Path.GetTempPath(), $"dnf-it-{Guid.NewGuid():N}");
        StoragePath = Path.Combine(_workDir, "media");
        Directory.CreateDirectory(_workDir);

        var postgres = Environment.GetEnvironmentVariable("DNF_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(postgres))
        {
            Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING", $"Data Source={Path.Combine(_workDir, "cms.db")}");
        }
        else
        {
            Environment.SetEnvironmentVariable("DATABASE_CONNECTION_STRING",
                $"{postgres.TrimEnd(';')};Database=dnf_it_{Guid.NewGuid():N}");
        }

        Environment.SetEnvironmentVariable("APP_URL", "http://localhost");
        Environment.SetEnvironmentVariable("STORAGE_S3_SERVICE_URL", "http://127.0.0.1:9");
        Environment.SetEnvironmentVariable("STORAGE_S3_BUCKET", "test");
        Environment.SetEnvironmentVariable("STORAGE_S3_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("STORAGE_S3_SECRET_ACCESS_KEY", "test");
    }

    /// <summary>Where this factory's uploaded media is stored.</summary>
    public string StoragePath { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);
        builder.ConfigureTestServices(services => services.AddSingleton<IFileStorage>(new LocalFileStorage(StoragePath)));
    }

    /// <summary>Programmatically completes installation (creates the first Super Admin).</summary>
    public async Task InstallAsync()
    {
        using var scope = Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInstallationStore>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var cache = scope.ServiceProvider.GetRequiredService<InstallationStatusCache>();

        await store.InstallFirstAdminAsync(new User
        {
            Email = AdminEmail,
            PasswordHash = hasher.Hash(AdminPassword),
            EmailConfirmed = true,
        });
        cache.MarkInstalled();
    }

    /// <summary>Creates an enabled user with one role in the default tenant and returns its id.</summary>
    public async Task<Guid> CreateUserAsync(string email, string password, string role)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        var tenant = await db.Tenants.OrderBy(t => t.CreatedDate).FirstAsync();
        var roleId = await db.Roles.Where(r => r.TenantId == tenant.Id && r.Name == role).Select(r => r.Id).FirstAsync();

        var user = new User { Email = email, PasswordHash = hasher.Hash(password), TenantId = tenant.Id };
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// <summary>Runs <paramref name="action"/> against a fresh DbContext scope.</summary>
    public async Task WithDbAsync(Func<DotNetForgeDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>());
    }

    /// <summary>
    /// A client that keeps cookies but doesn't follow redirects, so tests can assert them. HTTPS, because outside
    /// Development the auth cookie is Secure-only.
    /// </summary>
    public HttpClient CreateNoRedirectClient() =>
        CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true,
            BaseAddress = new Uri("https://localhost"),
        });

    /// <summary>A cookie-keeping client signed in through the real login form.</summary>
    public async Task<HttpClient> SignInAsync(string email = AdminEmail, string password = AdminPassword)
    {
        var client = CreateNoRedirectClient();
        var response = await PostFormAsync(client, "/account/login", "/account/login",
            new Dictionary<string, string> { ["email"] = email, ["password"] = password });
        if (response.StatusCode != HttpStatusCode.Redirect)
        {
            throw new InvalidOperationException($"Sign-in as {email} failed with {(int)response.StatusCode}.");
        }

        return client;
    }

    /// <summary>GETs <paramref name="formPage"/> for an antiforgery token, then POSTs the form to <paramref name="action"/>.</summary>
    public static async Task<HttpResponseMessage> PostFormAsync(
        HttpClient client, string formPage, string action, IDictionary<string, string> fields)
    {
        var token = await GetAntiforgeryTokenAsync(client, formPage);
        var content = new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token)));
        return await client.PostAsync(action, content);
    }

    public static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string formPage)
    {
        var html = await client.GetStringAsync(formPage);
        var match = TokenField().Match(html);
        return match.Success
            ? WebUtility.HtmlDecode(match.Groups[1].Value)
            : throw new InvalidOperationException($"No antiforgery token on {formPage}.");
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"")]
    private static partial Regex TokenField();

    protected override void Dispose(bool disposing)
    {
        if (disposing && Environment.GetEnvironmentVariable("DNF_TEST_POSTGRES") is { Length: > 0 })
        {
            try
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>().Database.EnsureDeleted();
            }
            catch
            {
                // Best effort: a leftover test database is harmless.
            }
        }

        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
                Directory.Delete(_workDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup of the temp directory.
            }
        }
    }
}
