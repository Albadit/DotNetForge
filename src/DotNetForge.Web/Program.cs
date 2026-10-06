using DotNetForge.Abstractions.Database;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Shared.Configuration;
using DotNetForge.Web.Middleware;
using DotNetForge.Web.Startup;

var builder = WebApplication.CreateBuilder(args);

// 1. Load and validate the .env configuration contract. Missing/invalid config aborts startup with a clear,
//    actionable message (.docs/features/configuration.md). Outside Development nothing may default to a path
//    inside the (read-only) deployment directory.
//    Database settings are validated by their providers while services are registered.
AppEnvironment env;
try
{
    env = EnvConfigurationLoader.Load(builder.Environment.ContentRootPath, builder.Environment.IsDevelopment());
    builder.Services.AddDotNetForge(env, builder.Environment);
}
catch (Exception ex) when (ex is ConfigurationException or DatabaseConfigurationException)
{
    Console.Error.WriteLine($"[DotNetForge] Configuration error: {ex.Message}");
    return 1;
}

var app = builder.Build();

// 2. Bring the database up to date and seed baseline data before serving traffic.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
    var catalog = scope.ServiceProvider.GetRequiredService<DatabaseCatalog>();
    await DatabaseInitializer.InitializeAsync(db, catalog.Main.Provider, app.Lifetime.ApplicationStopping);
}

// 3. Middleware pipeline. Behind a TLS-terminating proxy set ASPNETCORE_FORWARDEDHEADERS_ENABLED=true so the
//    scheme and client IP (rate limits, audit) come from the proxy's X-Forwarded-* headers.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseStaticFiles();
app.UseRouting();

// Install detection gates everything except the setup wizard until setup completes.
app.UseMiddleware<InstallationMiddleware>();

app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = env.AppName }));

// Any unmatched (non-file) URL resolves to a published public page by slug (e.g. /home, /about). The
// admin (/admin/*), setup (/setup), and API routes are all real controller routes, so they win over this.
app.MapFallbackToController("RenderPage", "Home");

app.Run();
return 0;

// Exposed so DotNetForge.IntegrationTests can drive the host with WebApplicationFactory<Program>.
public partial class Program { }
