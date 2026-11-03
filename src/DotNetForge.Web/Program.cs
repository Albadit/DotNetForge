using DotNetForge.Abstractions.Database;
using DotNetForge.Data;
using DotNetForge.Data.Database;
using DotNetForge.Infrastructure.Configuration;
using DotNetForge.Shared.Configuration;
using DotNetForge.Web.ApiDocs;
using DotNetForge.Web.Middleware;
using DotNetForge.Web.Startup;

// A published app has wwwroot (and extensions/) next to its dll: that folder is its content root even when it is started
// from another one (`dotnet out/DotNetForge.Web.dll`). A build in bin/ has none - dotnet run and the tests keep the default.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot")) ? AppContext.BaseDirectory : null,
});

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

app.UseResponseCompression();
// Inside compression: a screen asked for again unchanged is answered 304 (navigation.js keeps its copy).
app.UseMiddleware<ScreenUnchangedMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
// A Release build serves the Brotli/gzip copies its publish wrote instead of compressing each request.
if (BuildConfiguration.IsRelease)
{
    app.UseMiddleware<PrecompressedStaticFilesMiddleware>();
}

app.UseStaticFiles(new StaticFileOptions
{
    ContentTypeProvider = PrecompressedStaticFiles.ContentTypes,
    // asp-append-version: ?v= is a hash of the file, so that URL's content never changes and needs no checking again.
    OnPrepareResponse = file =>
    {
        PrecompressedStaticFiles.Prepare(file);
        if (file.Context.Request.Query.ContainsKey("v"))
        {
            file.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        }
    },
});
app.UseRouting();

// Install detection gates everything except the setup wizard until setup completes.
app.UseMiddleware<InstallationMiddleware>();

// Endpoints contributed by an extension answer only while it is enabled (.docs/features/extensions.md).
app.UseMiddleware<ExtensionEndpointGateMiddleware>();

// The built-in OAuth providers' schemes follow their Providers settings (re-read at most every 30 s).
app.UseMiddleware<DotNetForge.Web.Authentication.OAuthProviderRefreshMiddleware>();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapGet("/health", () => Results.Ok(new { status = "ok", app = env.AppName }));

// The API docs: the OpenAPI document and Scalar, for Super Admins and Developers (.docs/features/api-docs.md).
app.MapApiDocs(env);

// Each loaded extension's own endpoints (IExtension.MapEndpoints), e.g. /sitemap.xml from a plugin.
await app.MapDotNetForgeExtensionsAsync();

// Any unmatched (non-file) URL resolves to a published public page by slug (e.g. /home, /about). The
// admin (/admin/*), setup (/setup), and API routes are all real controller routes, so they win over this.
app.MapFallbackToController("RenderPage", "Home");

app.Run();
return 0;

// Exposed so DotNetForge.IntegrationTests can drive the host with WebApplicationFactory<Program>.
public partial class Program { }
