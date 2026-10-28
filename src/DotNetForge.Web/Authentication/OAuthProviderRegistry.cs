using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetForge.Data;
using DotNetForge.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DotNetForge.Web.Authentication;

/// <summary>
/// A provider's saved settings (<c>AuthProvider.SettingsJson</c>). <see cref="ClientSecret"/> and
/// <see cref="PrivateKey"/> are stored protected with Data Protection (the database key ring) and are only
/// unprotected in memory here.
/// </summary>
public sealed record ProviderSettings(string? ClientId, string? ClientSecret, string? TeamId, string? KeyId, string? PrivateKey,
    string? ServerUrl, string? Tenant)
{
    public static readonly ProviderSettings Empty = new(null, null, null, null, null, null, null);

    private const string Purpose = "DotNetForge.AuthProviders.Secrets.v1";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The value of a field (secrets in clear).</summary>
    public string? this[ProviderField field] => field switch
    {
        ProviderField.ClientId => ClientId,
        ProviderField.ClientSecret => ClientSecret,
        ProviderField.TeamId => TeamId,
        ProviderField.KeyId => KeyId,
        ProviderField.PrivateKey => PrivateKey,
        ProviderField.ServerUrl => ServerUrl,
        ProviderField.Tenant => Tenant,
        _ => null,
    };

    /// <summary>Every field the provider needs has a value (GitLab's server and Microsoft's tenant have defaults).</summary>
    public bool IsComplete(OAuthProviderDefinition provider) =>
        provider.Fields.Where(f => f is not (ProviderField.ServerUrl or ProviderField.Tenant)).All(f => !string.IsNullOrWhiteSpace(this[f]));

    /// <summary>
    /// Reads the stored JSON. A secret that can't be unprotected (an export from another site, whose key ring isn't
    /// here) reads as missing, and <paramref name="unreadable"/> says so.
    /// </summary>
    public static ProviderSettings Read(string? json, IDataProtectionProvider dataProtection, out bool unreadable)
    {
        unreadable = false;
        if (string.IsNullOrWhiteSpace(json) || json.Trim() == "{}")
        {
            return Empty;
        }

        var stored = JsonSerializer.Deserialize<ProviderSettings>(json, Json) ?? Empty;
        var protector = dataProtection.CreateProtector(Purpose);
        var failed = false;
        string? Unprotect(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return null;
            }

            try
            {
                return protector.Unprotect(value);
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                failed = true;
                return null;
            }
        }

        var settings = stored with { ClientSecret = Unprotect(stored.ClientSecret), PrivateKey = Unprotect(stored.PrivateKey) };
        unreadable = failed;
        return settings;
    }

    public string Write(IDataProtectionProvider dataProtection)
    {
        var protector = dataProtection.CreateProtector(Purpose);
        return JsonSerializer.Serialize(this with
        {
            ClientSecret = string.IsNullOrEmpty(ClientSecret) ? null : protector.Protect(ClientSecret),
            PrivateKey = string.IsNullOrEmpty(PrivateKey) ? null : protector.Protect(PrivateKey),
        }, Json);
    }
}

/// <summary>An enabled, fully configured built-in provider.</summary>
public sealed record ActiveOAuthProvider(OAuthProviderDefinition Definition, ProviderSettings Settings);

/// <summary>
/// Keeps the authentication schemes of the built-in providers in step with their <c>AuthProvider</c> rows
/// (.docs/features/authentication.md#built-in-oauth-providers): an enabled, complete provider has its scheme
/// (<c>oauth-google</c>, callback <c>/signin/google</c>); any other has none, so its callback answers 404. Remote
/// schemes are consulted on every request, which is why a half-configured one must not exist. The rows are re-read
/// at most every 30 seconds (<see cref="OAuthProviderRefreshMiddleware"/>) and at once after a save on this instance, so
/// changes need no restart and reach every instance.
/// </summary>
public sealed class OAuthProviderRegistry
{
    public static readonly TimeSpan ReloadInterval = TimeSpan.FromSeconds(30);

    private readonly IAuthenticationSchemeProvider _schemes;
    private readonly IOptionsMonitorCache<OAuthProviderOptions> _options;
    private readonly IDataProtectionProvider _dataProtection;
    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<OAuthProviderRegistry> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IReadOnlyDictionary<string, ActiveOAuthProvider> _active = new Dictionary<string, ActiveOAuthProvider>();
    private string? _stamp;
    private DateTime _loadedAt = DateTime.MinValue;

    public OAuthProviderRegistry(IAuthenticationSchemeProvider schemes, IOptionsMonitorCache<OAuthProviderOptions> options,
        IDataProtectionProvider dataProtection, IServiceScopeFactory scopes, ILogger<OAuthProviderRegistry> logger)
    {
        _schemes = schemes;
        _options = options;
        _dataProtection = dataProtection;
        _scopes = scopes;
        _logger = logger;
    }

    /// <summary>The providers the sign-in screen offers, in catalog order.</summary>
    public IReadOnlyList<ActiveOAuthProvider> Active =>
        OAuthProviderCatalog.All.Select(p => _active.GetValueOrDefault(p.Key)).OfType<ActiveOAuthProvider>().ToList();

    public ActiveOAuthProvider? FindByScheme(string? scheme) =>
        OAuthProviderCatalog.FindByScheme(scheme) is { } provider ? _active.GetValueOrDefault(provider.Key) : null;

    /// <summary>Re-read on the next request (after a save).</summary>
    public void Invalidate() => _loadedAt = DateTime.MinValue;

    public async Task RefreshIfStaleAsync(CancellationToken ct)
    {
        if (DateTime.UtcNow - _loadedAt < ReloadInterval)
        {
            return;
        }

        await _lock.WaitAsync(ct);
        try
        {
            if (DateTime.UtcNow - _loadedAt < ReloadInterval)
            {
                return;
            }

            await LoadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Keep the schemes as they are and try again in a while; sign-in with a password is unaffected.
            _logger.LogError(ex, "Reading the sign-in providers failed; retrying in {Seconds} s.", ReloadInterval.TotalSeconds);
        }
        finally
        {
            _loadedAt = DateTime.UtcNow;
            _lock.Release();
        }
    }

    private async Task LoadAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DotNetForgeDbContext>();
        var rows = await db.AuthProviders.AsNoTracking()
            .Select(p => new { p.Key, p.Enabled, p.SettingsJson, p.UpdatedDate })
            .ToListAsync(ct);
        var stamp = string.Join('|', rows.OrderBy(r => r.Key, StringComparer.Ordinal).Select(r => $"{r.Key}:{r.Enabled}:{r.UpdatedDate.Ticks}"));
        if (stamp == _stamp)
        {
            return;
        }

        var active = new Dictionary<string, ActiveOAuthProvider>(StringComparer.Ordinal);
        foreach (var provider in OAuthProviderCatalog.All)
        {
            var row = rows.FirstOrDefault(r => r.Key == provider.Key);
            if (row is { Enabled: true } && ProviderSettings.Read(row.SettingsJson, _dataProtection, out _) is var settings && settings.IsComplete(provider))
            {
                active[provider.Key] = new ActiveOAuthProvider(provider, settings);
            }
        }

        _active = active;
        foreach (var provider in OAuthProviderCatalog.All)
        {
            _options.TryRemove(provider.Scheme); // rebuilt from the new settings on first use
            var exists = await _schemes.GetSchemeAsync(provider.Scheme) is not null;
            if (active.ContainsKey(provider.Key) && !exists)
            {
                _schemes.TryAddScheme(new AuthenticationScheme(provider.Scheme, provider.Name, typeof(OAuthProviderHandler)));
            }
            else if (!active.ContainsKey(provider.Key) && exists)
            {
                _schemes.RemoveScheme(provider.Scheme);
            }
        }

        _stamp = stamp;
        _logger.LogInformation("Sign-in providers: {Providers}.", active.Count == 0 ? "none" : string.Join(", ", active.Keys));
    }
}

/// <summary>Keeps <see cref="OAuthProviderRegistry"/> current before authentication runs (a timestamp check most of the time).</summary>
public sealed class OAuthProviderRefreshMiddleware
{
    private readonly RequestDelegate _next;

    public OAuthProviderRefreshMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, OAuthProviderRegistry registry)
    {
        await registry.RefreshIfStaleAsync(context.RequestAborted);
        await _next(context);
    }
}

/// <summary>Fills a built-in provider's scheme options from its saved settings.</summary>
public sealed class ConfigureOAuthProviderOptions : IConfigureNamedOptions<OAuthProviderOptions>
{
    /// <summary>The client secret of a provider that signs its own per request (Apple); the base options need one.</summary>
    private const string SignedPerRequest = "signed-per-request";

    private readonly OAuthProviderRegistry _registry;
    private readonly IHostEnvironment _environment;

    public ConfigureOAuthProviderOptions(OAuthProviderRegistry registry, IHostEnvironment environment)
    {
        _registry = registry;
        _environment = environment;
    }

    public void Configure(OAuthProviderOptions options) => Configure(Options.DefaultName, options);

    public void Configure(string? name, OAuthProviderOptions options)
    {
        if (_registry.FindByScheme(name) is not { } active)
        {
            return;
        }

        var (provider, settings) = (active.Definition, active.Settings);
        string Fill(string url) => url
            .Replace("{server}", (settings.ServerUrl ?? "https://gitlab.com").TrimEnd('/'), StringComparison.Ordinal)
            .Replace("{tenant}", settings.Tenant ?? "common", StringComparison.Ordinal);

        options.Definition = provider;
        options.ClientId = settings.ClientId!;
        options.ClientSecret = provider.SignedClientSecret ? SignedPerRequest : settings.ClientSecret!;
        options.TeamId = settings.TeamId;
        options.KeyId = settings.KeyId;
        options.PrivateKey = settings.PrivateKey;
        options.AuthorizationEndpoint = Fill(provider.AuthorizationEndpoint);
        options.TokenEndpoint = Fill(provider.TokenEndpoint);
        options.UserInformationEndpoint = provider.UserInformationEndpoint is null ? null! : Fill(provider.UserInformationEndpoint);
        options.CallbackPath = provider.CallbackPath;
        options.SignInScheme = ExternalLogin.SignInScheme;
        options.UsePkce = provider.Pkce;
        options.Scope.Clear();
        foreach (var scope in provider.Scopes)
        {
            options.Scope.Add(scope);
        }

        // A posted callback (Apple) is a cross-site POST: the correlation cookie must be SameSite=None, hence Secure.
        options.CorrelationCookie.SameSite = provider.FormPost ? SameSiteMode.None : SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = provider.FormPost || !_environment.IsDevelopment()
            ? CookieSecurePolicy.Always
            : CookieSecurePolicy.SameAsRequest;

        // Cancelled at the provider, a bad state, a failed exchange: back to the sign-in screen with a message.
        options.Events.OnRemoteFailure = context =>
        {
            context.HttpContext.RequestServices.GetRequiredService<ILogger<OAuthProviderHandler>>()
                .LogWarning(context.Failure, "Sign-in with {Provider} failed.", provider.Name);
            context.Response.Redirect("/account/external/failed?provider=" + Uri.EscapeDataString(provider.Key));
            context.HandleResponse();
            return Task.CompletedTask;
        };
    }
}
