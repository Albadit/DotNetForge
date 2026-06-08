using DotNetForge.Data;
using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Shared.Configuration;
using DotNetForge.Web.Middleware;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;

var builder = WebApplication.CreateBuilder(args);

// 1. Load and validate the .env configuration contract. Missing/invalid config aborts startup
//    with a clear, actionable message (installation_setup.md).
AppEnvironment env;
try
{
    env = EnvConfigurationLoader.Load(builder.Environment.ContentRootPath);
}
catch (ConfigurationException ex)
{
    Console.Error.WriteLine($"[DotNetForge] Configuration error: {ex.Message}");
    return 1;
}

builder.Services.AddDotNetForge(env, builder.Environment.ContentRootPath);

var app = builder.Build();

// 2. Bring the database up to date and seed baseline data before serving traffic.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
    await DatabaseInitializer.InitializeAsync(db, env, app.Lifetime.ApplicationStopping);
}

// 3. Middleware pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

// Install detection gates everything except the setup wizard until setup completes.
app.UseMiddleware<InstallationMiddleware>();

app.UseAuthentication();
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
