using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services.Localization;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Settings → Global Settings → Internationalization (.docs/pages/internationalization.md): the site's locales - add,
/// rename, enable/disable, make default, delete - and the admin language of the site. Reading needs Settings read, every
/// change Settings update (Super Admin and Admin by default); the rules live in <see cref="LocaleService"/> and
/// <see cref="AdminLanguageService"/>.
/// </summary>
[Route("admin/internationalization")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed class InternationalizationController : AdminControllerBase
{
    private readonly LocaleService _locales;
    private readonly AdminLanguageService _adminLanguage;

    public InternationalizationController(LocaleService locales, AdminLanguageService adminLanguage)
    {
        _locales = locales;
        _adminLanguage = adminLanguage;
    }

    /// <summary>The screen: the locales and the admin language; also the Add locale form (bound on add).</summary>
    public sealed class InternationalizationViewModel
    {
        public IReadOnlyList<LocaleRow> Locales { get; set; } = Array.Empty<LocaleRow>();
        public bool CanEdit { get; set; }

        /// <summary>The languages the admin can be shown in, and the site's choice.</summary>
        public IReadOnlyList<AdminLanguageOption> AdminLanguages { get; set; } = Array.Empty<AdminLanguageOption>();
        public string AdminLanguage { get; set; } = AdminTranslations.English;

        public string? Code { get; set; }
        public string? DisplayName { get; set; }
        public bool Enabled { get; set; } = true;
    }

    /// <summary>The edit form of one locale.</summary>
    public sealed class LocaleEditModel
    {
        public Guid Id { get; set; }
        public string Code { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public bool Enabled { get; set; }
        public bool IsDefault { get; set; }
        public int Pages { get; set; }
        public int Entries { get; set; }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await BuildAsync(new InternationalizationViewModel()));

    /// <summary>The Add locale screen (the header button of the list).</summary>
    [HttpGet("add")]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public IActionResult Add() => View(new InternationalizationViewModel());

    [HttpPost("add")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Add(InternationalizationViewModel input)
    {
        var error = await _locales.AddAsync(input.Code, input.DisplayName, input.Enabled, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            return View(input);
        }

        TempData["Success"] = T["Locale added."];
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Edit(Guid id)
    {
        var row = (await _locales.ListAsync(HttpContext.RequestAborted)).FirstOrDefault(l => l.Id == id);
        if (row is null)
        {
            return NotFound();
        }

        ViewData["CanEdit"] = Can(PermissionAreas.Settings, PermissionActions.Update);
        return View(new LocaleEditModel
        {
            Id = row.Id,
            Code = row.Code,
            DisplayName = row.DisplayName,
            Enabled = row.Enabled,
            IsDefault = row.IsDefault,
            Pages = row.Pages,
            Entries = row.Entries,
        });
    }

    [HttpPost("{id:guid}")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Edit(Guid id, LocaleEditModel input)
    {
        var error = await _locales.UpdateAsync(id, input.DisplayName, input.Enabled, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            ViewData["CanEdit"] = true;
            var row = (await _locales.ListAsync(HttpContext.RequestAborted)).FirstOrDefault(l => l.Id == id);
            if (row is null)
            {
                return NotFound();
            }

            input.Id = id;
            input.Code = row.Code;
            input.IsDefault = row.IsDefault;
            input.Pages = row.Pages;
            input.Entries = row.Entries;
            return View(input);
        }

        TempData["Success"] = T["Locale saved."];
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/enable")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public Task<IActionResult> Enable(Guid id) => SetEnabledAsync(id, true);

    [HttpPost("{id:guid}/disable")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public Task<IActionResult> Disable(Guid id) => SetEnabledAsync(id, false);

    [HttpPost("{id:guid}/default")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> SetDefault(Guid id)
    {
        var error = await _locales.SetDefaultAsync(id, HttpContext.RequestAborted);
        return Done(error, T["Default locale changed. Its translations are now the main content; the previous default's text became its translations."]);
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Delete(Guid id) =>
        Done(await _locales.DeleteAsync(id, HttpContext.RequestAborted), T["Locale deleted, with its translations."]);

    /// <summary>The site's admin language: what every admin user sees unless they chose their own on Profile.</summary>
    [HttpPost("admin-language")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> AdminLanguage(string? language)
    {
        var error = await _adminLanguage.SetSiteLanguageAsync(language, HttpContext.RequestAborted);
        if (error is null)
        {
            // This request still speaks the previous language; the toast shows in the new one.
            T.Use((await _adminLanguage.ResolveAsync(User, HttpContext.RequestAborted)).Code);
        }

        return Done(error, T["Admin language saved."]);
    }

    private async Task<IActionResult> SetEnabledAsync(Guid id, bool enabled)
    {
        var row = (await _locales.ListAsync(HttpContext.RequestAborted)).FirstOrDefault(l => l.Id == id);
        if (row is null)
        {
            return NotFound();
        }

        var error = await _locales.UpdateAsync(id, row.DisplayName, enabled, HttpContext.RequestAborted);
        return Done(error, enabled ? T["{0} enabled.", row.DisplayName] : T["{0} disabled. Its translations are kept.", row.DisplayName]);
    }

    private IActionResult Done(string? error, string success)
    {
        if (error is null)
        {
            TempData["Success"] = success;
        }
        else
        {
            TempData["Error"] = error;
        }

        return RedirectToAction(nameof(Index));
    }

    private async Task<InternationalizationViewModel> BuildAsync(InternationalizationViewModel model)
    {
        model.Locales = await _locales.ListAsync(HttpContext.RequestAborted);
        model.CanEdit = Can(PermissionAreas.Settings, PermissionActions.Update);
        model.AdminLanguages = _adminLanguage.Languages;
        model.AdminLanguage = (await _adminLanguage.SiteLanguageAsync(HttpContext.RequestAborted)).Code;
        return model;
    }
}
