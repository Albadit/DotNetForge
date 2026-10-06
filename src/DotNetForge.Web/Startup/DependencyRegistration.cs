using System.Security.Claims;
using System.Threading.RateLimiting;
using DotNetForge.Abstractions.Authorization;
using DotNetForge.Abstractions.Security;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Api.Authentication;
using DotNetForge.Api.Controllers;
using DotNetForge.Core.Authorization;
using DotNetForge.Core.Extensions;
using DotNetForge.Core.Installation;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Extensions;
using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Infrastructure.Security;
using DotNetForge.Infrastructure.Storage;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Enums;
using DotNetForge.Shared.Stores;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace DotNetForge.Web.Startup;

/// <summary>
/// Composition root wiring. Registers persistence, security primitives, storage, domain services, the extension
/// host, authentication (cookie for admin, token for the API), authorization policies and rate limits. Keeping all
/// DI in one place makes the dependency graph auditable (.docs/architecture/dependencies.md).
/// </summary>
public static class DependencyRegistration
{
    public const string AdminAreaPolicy = "AdminArea";

    /// <summary>How long a "this account is still enabled" check is remembered per user.</summary>
    private static readonly TimeSpan SessionValidationCacheLifetime = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddDotNetForge(
        this IServiceCollection services, AppEnvironment env, IWebHostEnvironment hostEnv)
    {
        services.AddSingleton(env);
        services.AddMemoryCache();

        // Persistence (.docs/database/architecture.md). Providers are registered by name; the configured databases
        // are resolved against them, the main database's provider registers DotNetForgeDbContext, and
        // IDatabaseService routes structured commands to any configured database. A new database = one more
        // AddDatabaseProvider line (.docs/database/adding-a-provider.md).
        services.AddDefaultDatabaseProviders();
        services.AddDotNetForgeDatabases(env.Database, env.AdditionalDatabases,
            new DatabaseHostContext(hostEnv.IsDevelopment(), AppPaths.DevelopmentDataRoot(hostEnv.ContentRootPath)));

        // The Data Protection key ring (cookies, antiforgery) lives in the database: the deployment filesystem is
        // read-only, and every instance must share the same keys.
        services.AddDataProtection()
            .SetApplicationName("DotNetForge")
            .PersistKeysToDbContext<DotNetForgeDbContext>();

        // Runtime file storage (uploaded media) - never the deployment directory.
        if (env.Storage.Provider == StorageProvider.S3)
        {
            services.AddSingleton<IFileStorage>(_ => new S3FileStorage(env.Storage));
        }
        else
        {
            services.AddSingleton<IFileStorage>(_ => new LocalFileStorage(env.Storage.LocalPath));
        }

        // Security primitives (BCL-only implementations).
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IDateTimeProvider, DotNetForge.Infrastructure.Security.SystemClock>();
        services.AddSingleton<IApiTokenFactory, ApiTokenFactory>();

        // Domain services.
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IManifestValidator, ManifestValidator>();
        services.AddSingleton<IExtensionLoader>(sp =>
            new ExtensionLoader(sp.GetRequiredService<IManifestValidator>(), env.ExtensionsPath));
        services.AddScoped<IInstallationStore, InstallationStore>();
        services.AddScoped<IInstallationService, InstallationService>();

        // Web-host application services.
        services.AddSingleton<InstallationStatusCache>();
        services.AddHttpContextAccessor();
        services.AddScoped<AuthService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IPageService, PageService>();
        services.AddScoped<MediaService>();

        // Authentication: cookie for the admin UI, bearer token for the headless API.
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/account/login";
                options.LogoutPath = "/account/logout";
                options.AccessDeniedPath = "/account/denied";
                options.ExpireTimeSpan = TimeSpan.FromHours(8);
                options.SlidingExpiration = true;
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                // Production sits behind HTTPS (directly or via a TLS-terminating proxy with forwarded headers).
                options.Cookie.SecurePolicy = hostEnv.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.Cookie.Name = "dnf.auth";
                options.Events.OnValidatePrincipal = ValidateSessionAsync;
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenDefaults.Scheme, _ => { });

        services.AddAuthorization(options =>
        {
            // Only admin-capable roles may enter the admin area (.docs/features/authorization.md).
            options.AddPolicy(AdminAreaPolicy, policy => policy.RequireRole(Roles.AdminCapable));
        });

        AddRateLimits(services);

        // Razor forms post the antiforgery token in the hidden field; the Content Manager's drag-and-drop
        // reorder (a fetch POST) sends it in this X-CSRF-TOKEN header instead.
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

        // Background job that applies page publish/unpublish schedules.
        services.AddHostedService<ScheduledPublishingService>();

        // MVC + the API controllers (the API project is mounted as an application part). Runtime Razor
        // compilation lets admin extensions ship views under extensions/ that are compiled in memory and rendered
        // on demand; the extra file provider is rooted at the folder that contains extensions/ (the app folder when
        // published, the repository root in a checkout), so views resolve as ~/extensions/... and nothing is written.
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(ContentApiController).Assembly)
            .AddRazorRuntimeCompilation(options =>
                options.FileProviders.Add(new PhysicalFileProvider(ExtensionViewRoot(env))));

        return services;
    }

    /// <summary>
    /// Ends a cookie session whose user was deleted or disabled. Roles stay as issued at sign-in; the check result is
    /// cached briefly per user so it costs at most one indexed query per user every 30 seconds.
    /// </summary>
    private static async Task ValidateSessionAsync(CookieValidatePrincipalContext context)
    {
        if (!Guid.TryParse(context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId))
        {
            context.RejectPrincipal();
            return;
        }

        var services = context.HttpContext.RequestServices;
        var cache = services.GetRequiredService<IMemoryCache>();
        var cacheKey = $"dnf:session:{userId}";
        if (!cache.TryGetValue(cacheKey, out bool active))
        {
            active = await services.GetRequiredService<AuthService>().IsActiveAsync(userId, context.HttpContext.RequestAborted);
            cache.Set(cacheKey, active, SessionValidationCacheLifetime);
        }

        if (!active)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Per-IP fixed-window limits (.docs/features/security.md). Rejected requests get 429.</summary>
    private static void AddRateLimits(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Sign-in and setup: enough for typing mistakes, too few for password guessing.
            options.AddPolicy(RateLimitPolicies.Credentials, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            options.AddPolicy(RateLimitPolicies.Api, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 300,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
    }

    /// <summary>The folder that contains <c>extensions/</c>; extension view paths are relative to it.</summary>
    public static string ExtensionViewRoot(AppEnvironment env) =>
        Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(env.ExtensionsPath))!;

    /// <summary>The client IP (behind a proxy, enable forwarded headers so this is the real client).</summary>
    private static string ClientKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
