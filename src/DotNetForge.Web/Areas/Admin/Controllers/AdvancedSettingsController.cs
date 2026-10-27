using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Advanced User Settings (.docs/pages/advanced-settings.md, .docs/features/authentication.md#advanced-user-settings):
/// whether visitors may sign up at <c>/register</c> (off by default), the role new accounts get, email confirmation
/// and the optional front-end URLs of the email links. Reading needs Settings read, saving Settings update; the rules
/// live in <see cref="UserAccountSettingsService"/>.
/// </summary>
[Route("admin/advanced-settings")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed class AdvancedSettingsController : AdminControllerBase
{
    private readonly UserAccountSettingsService _settings;
    private readonly DotNetForgeDbContext _db;
    private readonly IEmailSettingsSource _email;

    public AdvancedSettingsController(UserAccountSettingsService settings, DotNetForgeDbContext db, IEmailSettingsSource email)
    {
        _settings = settings;
        _db = db;
        _email = email;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var current = await _settings.GetAsync(HttpContext.RequestAborted);
        return View(await BuildAsync(new AdvancedSettingsViewModel
        {
            SignUps = current.SignUps,
            DefaultRoleId = current.DefaultRoleId,
            EmailConfirmation = current.EmailConfirmation,
            ResetPasswordUrl = current.ResetPasswordUrl,
            ConfirmationRedirectUrl = current.ConfirmationRedirectUrl,
        }));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Save(AdvancedSettingsViewModel input)
    {
        // SaveAsync writes the SettingsChanged audit entry.
        var errors = await _settings.SaveAsync(
            new UserAccountSettings(input.SignUps, input.DefaultRoleId, input.EmailConfirmation, input.ResetPasswordUrl, input.ConfirmationRedirectUrl),
            User.IsInRole(Roles.SuperAdmin), HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(nameof(Index), await BuildAsync(input));
        }

        TempData["Success"] = "Advanced user settings saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<AdvancedSettingsViewModel> BuildAsync(AdvancedSettingsViewModel model)
    {
        var roles = await _db.Roles.AsNoTracking()
            .Where(r => r.Name != Roles.SuperAdmin && r.Name != Roles.Public)
            .Select(r => new { r.Id, r.Name })
            .ToListAsync(HttpContext.RequestAborted);
        model.Roles = roles
            .OrderBy(r => r.Name == Roles.Authenticated ? 0 : 1).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Select(r => (r.Id, r.Name, Roles.CanEnterAdmin(new[] { r.Name })))
            .ToList();
        model.EmailConfigured = (await _email.GetAsync(HttpContext.RequestAborted)).IsConfigured;
        model.CanEdit = Can(PermissionAreas.Settings, PermissionActions.Update);
        return model;
    }
}
