using DotNetForge.Abstractions.Authorization;
using DotNetForge.Abstractions.Messaging;
using DotNetForge.Abstractions.Security;
using DotNetForge.Api.Authentication;
using DotNetForge.Api.Controllers;
using DotNetForge.Core.Authorization;
using DotNetForge.Core.Extensions;
using DotNetForge.Core.Installation;
using DotNetForge.Data;
using DotNetForge.Extensions;
using DotNetForge.Infrastructure.Messaging;
using DotNetForge.Infrastructure.Security;
using DotNetForge.Infrastructure.Storage;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Stores;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Startup;

/// <summary>
/// Composition root wiring. Registers persistence, security primitives, domain services, the
/// extension host, authentication (cookie for admin, token for the API), and authorization policies.
/// Keeping all DI in one place makes the dependency graph auditable (architecture.md).
/// </summary>
public static class DependencyRegistration
{
    public const string AdminAreaPolicy = "AdminArea";

    public static IServiceCollection AddDotNetForge(
        this IServiceCollection services, AppEnvironment env, string contentRoot)
    {
        services.AddSingleton(env);

        // Persistence (provider chosen from .env).
        services.AddDbContext<DotNetForgeDbContext>(options => DbProviderConfigurator.Configure(options, env));

        // Security primitives & infrastructure (BCL-only implementations).
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IDateTimeProvider, SystemClock>();
        services.AddSingleton<IWebhookSigner, HmacWebhookSigner>();
        services.AddSingleton<IApiTokenFactory, ApiTokenFactory>();
        services.AddSingleton<IEmailSender>(_ =>
            new FileSystemEmailSender(Path.Combine(contentRoot, "storage", "logs", "email")));
        services.AddSingleton<IFileStorage>(_ =>
            new LocalFileStorage(Path.Combine(contentRoot, "storage", "media")));

        // Domain services.
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddSingleton<IManifestValidator, ManifestValidator>();
        services.AddSingleton<IExtensionLoader, ExtensionLoader>();
        services.AddScoped<IInstallationStore, InstallationStore>();
        services.AddScoped<IInstallationService, InstallationService>();

        // Web-host application services.
        services.AddSingleton<InstallationStatusCache>();
        services.AddHttpContextAccessor();
        services.AddScoped<AuthService>();
        services.AddScoped<AuditService>();

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
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Cookie.Name = "dnf.auth";

                // The admin SPA expects JSON 401/403 from /admin-api, not browser redirects.
                options.Events.OnRedirectToLogin = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/admin-api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
                options.Events.OnRedirectToAccessDenied = ctx =>
                {
                    if (ctx.Request.Path.StartsWithSegments("/admin-api"))
                    {
                        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                        return Task.CompletedTask;
                    }

                    ctx.Response.Redirect(ctx.RedirectUri);
                    return Task.CompletedTask;
                };
            })
            .AddScheme<Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions, ApiTokenAuthenticationHandler>(
                ApiTokenDefaults.Scheme, _ => { });

        services.AddAuthorization(options =>
        {
            // Only admin-capable roles may enter the admin area (admin_area.md).
            options.AddPolicy(AdminAreaPolicy, policy =>
                policy.RequireRole(Roles.SuperAdmin, Roles.Admin, Roles.Editor, Roles.Author));
        });

        // Antiforgery for the admin SPA: the token is sent back in an X-CSRF-TOKEN header on writes.
        services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");

        // MVC + the API controllers (the API project is mounted as an application part).
        services.AddControllersWithViews()
            .AddApplicationPart(typeof(ContentApiController).Assembly);

        return services;
    }
}
