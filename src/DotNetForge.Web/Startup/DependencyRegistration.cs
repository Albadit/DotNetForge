using System.Security.Claims;
using System.Threading.RateLimiting;
using DotNetForge.Abstractions.Authorization;
using DotNetForge.Abstractions.Security;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Api.Authentication;
using DotNetForge.Api.Controllers;
using DotNetForge.Content;
using DotNetForge.Core.Authorization;
using DotNetForge.Core.Extensions;
using DotNetForge.Core.Installation;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Extensions;
using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Infrastructure.Messaging;
using DotNetForge.Infrastructure.Security;
using DotNetForge.Infrastructure.Storage;
using DotNetForge.Sdk;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Enums;
using DotNetForge.Shared.Stores;
using DotNetForge.Web.ApiDocs;
using DotNetForge.Web.Authentication;
using DotNetForge.Web.Services;
using DotNetForge.Web.Services.Mfa;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.FileProviders;

namespace DotNetForge.Web.Startup;

/// <summary>
/// Composition root wiring. Registers persistence, security primitives, storage, domain services, the extension
/// host (and every loaded extension's own services), authentication (cookie for admin, token for the API),
/// authorization policies and rate limits. Keeping all DI in one place makes the dependency graph auditable
/// (.docs/architecture/dependencies.md).
/// </summary>
public static class DependencyRegistration
{
    public const string AdminAreaPolicy = AuthorizationPolicies.AdminArea;

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

        // Runtime file storage (uploaded media) - never the deployment directory. The host's own storage is keyed;
        // IFileStorage routes to an enabled storage-connector extension, else to it (.docs/features/media-storage.md).
        if (env.Storage.Provider == StorageProvider.S3)
        {
            services.AddKeyedSingleton<IFileStorage>(ExtensionMediaStorage.CoreStorageKey, (_, _) => new S3FileStorage(env.Storage));
        }
        else
        {
            services.AddKeyedSingleton<IFileStorage>(ExtensionMediaStorage.CoreStorageKey, (_, _) => new LocalFileStorage(env.Storage.LocalPath));
        }

        services.AddSingleton<IFileStorage>(sp => new ExtensionMediaStorage(
            sp.GetRequiredKeyedService<IFileStorage>(ExtensionMediaStorage.CoreStorageKey),
            sp.GetServices<MediaStorageContribution>(), sp.GetRequiredService<ExtensionCatalog>(), sp));

        // Security primitives (BCL-only implementations).
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IDateTimeProvider, DotNetForge.Infrastructure.Security.SystemClock>();
        services.AddSingleton<IApiTokenFactory, ApiTokenFactory>();

        // Domain services.
        // Authorization reads the roles' stored grants (cached), so custom roles and edits take effect.
        services.AddSingleton<IRoleGrantStore, RoleGrantStore>();
        services.AddSingleton<IPermissionService>(sp => new PermissionService(sp.GetRequiredService<IRoleGrantStore>()));
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IInstallationStore, InstallationStore>();
        services.AddScoped<IInstallationService, InstallationService>();

        // Web-host application services.
        services.AddSingleton<InstallationStatusCache>();
        services.AddHttpContextAccessor();
        services.AddScoped<AuthService>();
        // Self-service accounts and the Advanced User Settings (.docs/features/authentication.md). Email over SMTP with
        // SMTP_* from .env (.docs/features/email.md); every send goes through LoggingEmailSender (the email log).
        services.AddScoped<EmailConfigurationService>();
        services.AddScoped<EmailTemplateService>();
        services.AddScoped<IEmailSettingsSource>(sp => sp.GetRequiredService<EmailConfigurationService>());
        services.AddKeyedScoped<DotNetForge.Abstractions.Messaging.IEmailSender, SmtpEmailSender>(LoggingEmailSender.TransportKey);
        services.AddScoped<DotNetForge.Abstractions.Messaging.IEmailSender, LoggingEmailSender>();
        services.AddScoped<UserAccountSettingsService>();
        services.AddScoped<AccountService>();
        // Two-factor authentication (.docs/features/mfa.md): settings, methods, sign-in step; SMS through Twilio.
        services.AddScoped<MfaSettingsService>();
        services.AddScoped<MfaService>();
        services.AddHttpClient<ISmsSender, TwilioSmsSender>(client => client.Timeout = TimeSpan.FromSeconds(15));
        // Built-in OAuth providers (.docs/features/authentication.md#built-in-oauth-providers): settings on Providers,
        // schemes added and removed at runtime by the registry.
        services.AddScoped<AuthProviderService>();
        services.AddSingleton<OAuthProviderRegistry>();
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<OAuthProviderOptions>, ConfigureOAuthProviderOptions>();
        services.AddSingleton<Microsoft.Extensions.Options.IPostConfigureOptions<OAuthProviderOptions>,
            OAuthPostConfigureOptions<OAuthProviderOptions, OAuthProviderHandler>>();
        // Data Management: export, backups (stored with IFileStorage) and the Development-only import (.docs/pages/data-management.md).
        services.AddScoped<DatabaseTransferService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IPageService, PageService>();
        services.AddScoped<IPublishedPages, PublishedPageService>();
        services.AddScoped<MediaService>();
        services.AddScoped<MediaFolderService>();
        // Media Library settings: upload limits and optimization (image re-encoding, ffmpeg video compression).
        services.AddScoped<MediaSettingsService>();
        services.AddScoped<MediaExportService>();
        services.AddSingleton<Ffmpeg>();
        services.AddSingleton<VideoOptimizationQueue>();
        services.AddScoped<UserManagementService>();
        services.AddScoped<RoleManagementService>();
        services.AddScoped<ProfileService>();
        services.AddScoped<PagePermissionService>();
        services.AddScoped<PageBuilderService>();
        // Structured content (.docs/features/structured-content.md).
        services.AddScoped<ContentTypeService>();
        services.AddScoped<ContentEntryService>();
        services.AddScoped<ContentQueryService>();
        services.AddScoped<ContentRenderService>();
        services.AddScoped<ContentApiService>();
        // Custom API endpoints (.docs/features/custom-endpoints.md).
        services.AddScoped<DotNetForge.Content.Endpoints.CustomEndpointService>();
        services.AddScoped<DotNetForge.Content.Endpoints.SqlEndpointRunner>();

        // API docs (.docs/features/api-docs.md): the OpenAPI document of the headless API and the custom endpoints,
        // rendered by Scalar at /admin/api-docs.
        services.AddOpenApi(ApiDocsDocument.Name, options =>
        {
            options.ShouldInclude = description => ApiDocsDocument.Includes(description.RelativePath);
            options.AddDocumentTransformer<ApiDocsDocumentTransformer>();
            options.AddOperationTransformer<ApiDocsOperationTransformer>();
        });
        services.AddScoped<DotNetForge.Web.Areas.Admin.Navigation.AdminNavigationService>();

        var extensions = AddExtensionHost(services, env);

        // Authentication: cookie for the admin UI, bearer token for the headless API.
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/login";
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
            // Short-lived cookie that carries an authentication extension's result to ExternalLoginController.
            .AddCookie(ExternalLogin.SignInScheme, options =>
            {
                options.Cookie.Name = "dnf.external";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = hostEnv.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            })
            // Short-lived cookie of a sign-in waiting for its second factor (PendingSignIn, MfaLoginController).
            .AddCookie(PendingSignIn.Scheme, options =>
            {
                options.Cookie.Name = "dnf.mfa";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = hostEnv.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
                options.ExpireTimeSpan = PendingSignIn.Lifetime;
                options.SlidingExpiration = false;
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenDefaults.Scheme, _ => { })
            // The API's scheme: a token when one is sent, otherwise a Super Admin's same-origin admin session (API docs).
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, AdminSessionAuthenticationHandler>(
                ApiTokenDefaults.SessionScheme, _ => { })
            .AddPolicyScheme(ApiTokenDefaults.RequestScheme, ApiTokenDefaults.RequestScheme, options =>
                options.ForwardDefaultSelector = context => HasCredentials(context.Request.Headers.Authorization.ToString())
                    ? ApiTokenDefaults.Scheme
                    : ApiTokenDefaults.SessionScheme);

        services.AddAuthorization(options =>
        {
            // Every role except the site-only ones (Authenticated, Public) may enter the admin area - built-in and custom;
            // each screen then checks its own permission (.docs/features/authorization.md#screen-access).
            options.AddPolicy(AuthorizationPolicies.AdminArea, policy => policy.RequireAuthenticatedUser().RequireAssertion(
                context => Roles.CanEnterAdmin(context.User.FindAll(ClaimTypes.Role).Select(c => c.Value))));
            options.AddPolicy(AuthorizationPolicies.ApiDocs, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.SuperAdmin, Roles.Developer));
        });

        AddRateLimits(services);

        // Razor forms post the antiforgery token in the hidden field; the Content Manager's drag-and-drop
        // reorder (a fetch POST) sends it in this X-CSRF-TOKEN header instead.
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

        // Background job that applies page publish/unpublish schedules.
        services.AddHostedService<ScheduledPublishingService>();
        // Background job that stores automatic backups on the Data Management schedule (off by default).
        services.AddHostedService<AutomaticBackupService>();
        // Background job that compresses queued videos with ffmpeg (only with FFMPEG_PATH and compression on).
        services.AddHostedService<VideoOptimizationService>();

        // MVC + the API controllers (the API project is mounted as an application part). Runtime Razor
        // compilation lets extensions ship views under extensions/ that are compiled in memory and rendered on
        // demand; the extra file provider is rooted at the folder that contains extensions/ (the app folder when
        // published, the repository root in a checkout), so views resolve as ~/extensions/... and nothing is written.
        // Extension assemblies are application parts too (their controllers) and compile references for their views.
        var loaded = extensions.Extensions.Where(e => e.IsLoaded).ToList();
        var mvc = services.AddControllersWithViews(options => options.Conventions.Add(new ExtensionControllerConvention(extensions)))
            .AddApplicationPart(typeof(ContentApiController).Assembly)
            .AddRazorRuntimeCompilation(options =>
            {
                options.FileProviders.Add(new PhysicalFileProvider(ExtensionViewRoot(env)));
                foreach (var path in loaded.SelectMany(e => e.ReferencePaths).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    options.AdditionalReferencePaths.Add(path);
                }
            });
        mvc.ConfigureApplicationPartManager(parts =>
        {
            foreach (var extension in loaded)
            {
                parts.ApplicationParts.Add(new ExtensionAssemblyPart(extension.Assembly!));
            }
        });

        return services;
    }

    /// <summary>
    /// The extension host (.docs/features/extensions.md): discovery, code loading - done here, before the container
    /// is built, because each loaded extension registers its own services - enabled state, the catalog and the slot
    /// services. An extension whose <c>ConfigureServices</c> throws is marked failed and contributes nothing.
    /// </summary>
    private static ExtensionRuntime AddExtensionHost(IServiceCollection services, AppEnvironment env)
    {
        var validator = new ManifestValidator();
        var loader = new ExtensionLoader(validator, env.ExtensionsPath);
        var runtime = ExtensionRuntime.Load(loader.Discover(), env.Lookup);

        services.AddSingleton<IManifestValidator>(validator);
        services.AddSingleton<IExtensionLoader>(_ => loader); // factory registration: the container disposes the watcher
        services.AddSingleton(runtime);
        services.AddSingleton<ExtensionState>();
        services.AddSingleton<IExtensionState>(sp => sp.GetRequiredService<ExtensionState>());
        // Resolving the loader through the container (not the captured instance) makes the container dispose it.
        services.AddSingleton(sp => new ExtensionCatalog(sp.GetRequiredService<IExtensionLoader>(), runtime,
            sp.GetRequiredService<IExtensionState>(), env.Lookup));
        services.AddScoped<ExtensionLifecycleService>();
        services.AddSingleton<ExtensionViewRenderer>();
        services.AddScoped<ThemeResolver>();
        services.AddSingleton<LoginProviders>();
        services.AddSingleton<AuditEventDispatcher>();
        services.AddHostedService(sp => sp.GetRequiredService<AuditEventDispatcher>());

        foreach (var extension in runtime.Extensions.Where(e => e.IsLoaded))
        {
            try
            {
                extension.Instance!.ConfigureServices(services, extension.Context);
            }
            catch (Exception ex)
            {
                // Extension code: record the failure (Plugins screen, startup log) instead of failing startup.
                extension.Fail($"ConfigureServices failed: {ex.Message}");
            }
        }

        return runtime;
    }

    /// <summary>
    /// Whether an <c>Authorization</c> header carries a credential. An empty one, or a bare "Bearer" (what an API
    /// client sends when its token field is left empty), falls back to the admin session.
    /// </summary>
    private static bool HasCredentials(string authorization)
    {
        var value = authorization.Trim();
        return value.Length > 0 && !value.Equals("Bearer", StringComparison.OrdinalIgnoreCase);
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

            // Public form posts from extensions (contact forms): a person sends a few, a spam bot many.
            options.AddPolicy(RateLimitPolicies.Forms, http => RateLimitPartition.GetFixedWindowLimiter(
                ClientKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
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
