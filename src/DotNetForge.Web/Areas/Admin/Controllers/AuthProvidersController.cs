using DotNetForge.Extensions;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Authentication;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Users &amp; Permissions Plugin → Providers (.docs/pages/providers.md): the sign-in providers - Email (password
/// sign-in, built in), the ten built-in OAuth providers of <see cref="OAuthProviderCatalog"/> with their settings, and
/// the providers of authentication extensions (read-only; configured in <c>.env</c>, turned on on Plugins). Reading
/// needs Settings read, saving Settings update; the rules live in <see cref="AuthProviderService"/>.
/// </summary>
[Route("admin/providers")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed class AuthProvidersController : AdminControllerBase
{
    private readonly AuthProviderService _providers;
    private readonly LoginProviders _loginProviders;
    private readonly ExtensionCatalog _extensions;
    private readonly AppEnvironment _env;

    public AuthProvidersController(AuthProviderService providers, LoginProviders loginProviders, ExtensionCatalog extensions, AppEnvironment env)
    {
        _providers = providers;
        _loginProviders = loginProviders;
        _extensions = extensions;
        _env = env;
    }

    public enum ProviderState
    {
        Enabled,
        Disabled,
        NotSetUp,
    }

    public sealed record ProviderRow(OAuthProviderDefinition Provider, ProviderState State, bool Unreadable);

    public sealed record ExtensionRow(string Name, string Scheme, string ExtensionId, bool Active);

    public sealed record ProvidersViewModel(IReadOnlyList<ProviderRow> Providers, IReadOnlyList<ExtensionRow> Extensions, string AppUrl);

    public sealed record ProviderEditViewModel(OAuthProviderDefinition Provider, ProviderInput Input, bool HasClientSecret, bool HasPrivateKey,
        bool Unreadable, string CallbackUrl, bool CanEdit);

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var all = await _providers.AllAsync(HttpContext.RequestAborted);
        var rows = OAuthProviderCatalog.All.Select(p =>
        {
            var (row, settings, unreadable) = all.TryGetValue(p.Key, out var r) ? r : default;
            var state = row is null || !settings.IsComplete(p) ? ProviderState.NotSetUp : row.Enabled ? ProviderState.Enabled : ProviderState.Disabled;
            return new ProviderRow(p, state, unreadable);
        }).ToList();

        var extensions = new List<ExtensionRow>();
        foreach (var provider in _loginProviders.ExtensionProviders)
        {
            extensions.Add(new ExtensionRow(provider.DisplayName, provider.Scheme, provider.ExtensionId,
                await _extensions.IsActiveAsync(provider.ExtensionId, HttpContext.RequestAborted)));
        }

        return View(new ProvidersViewModel(rows, extensions, _env.AppUrl.TrimEnd('/')));
    }

    [HttpGet("{key}")]
    public async Task<IActionResult> Edit(string key)
    {
        if (OAuthProviderCatalog.Find(key) is not { } provider)
        {
            return NotFound();
        }

        var all = await _providers.AllAsync(HttpContext.RequestAborted);
        var (row, settings, unreadable) = all.TryGetValue(provider.Key, out var r) ? r : default;
        settings ??= ProviderSettings.Empty;
        return View(Build(provider, new ProviderInput
        {
            Enabled = row?.Enabled == true,
            ClientId = settings.ClientId,
            TeamId = settings.TeamId,
            KeyId = settings.KeyId,
            ServerUrl = settings.ServerUrl,
            Tenant = settings.Tenant,
        }, settings, unreadable));
    }

    [HttpPost("{key}")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Edit(string key, ProviderInput input)
    {
        if (OAuthProviderCatalog.Find(key) is not { } provider)
        {
            return NotFound();
        }

        // SaveAsync writes the provider.updated audit entry and refreshes the sign-in schemes.
        var errors = await _providers.SaveAsync(provider, input, HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            var all = await _providers.AllAsync(HttpContext.RequestAborted);
            var (_, settings, unreadable) = all.TryGetValue(provider.Key, out var r) ? r : default;
            return View(Build(provider, input, settings ?? ProviderSettings.Empty, unreadable));
        }

        TempData["Success"] = input.Enabled
            ? $"{provider.Name} is on: the sign-in page shows \"Sign in with {provider.Name}\"."
            : $"{provider.Name} is saved and off.";
        return RedirectToAction(nameof(Index));
    }

    private ProviderEditViewModel Build(OAuthProviderDefinition provider, ProviderInput input, ProviderSettings saved, bool unreadable) =>
        new(provider, input, !string.IsNullOrEmpty(saved.ClientSecret), !string.IsNullOrEmpty(saved.PrivateKey), unreadable,
            _env.AppUrl.TrimEnd('/') + provider.CallbackPath, Can(PermissionAreas.Settings, PermissionActions.Update));
}
