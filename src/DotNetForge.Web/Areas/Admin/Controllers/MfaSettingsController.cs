using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services.Mfa;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Users &amp; Permissions Plugin → Two-factor authentication (.docs/pages/mfa.md): which second factors users may set
/// up - authenticator app, passkeys, email codes, SMS codes - and whether everyone who opens the admin must have one.
/// Reading needs Settings read, saving Settings update; the rules live in <see cref="MfaSettingsService"/>.
/// </summary>
[Route("admin/mfa")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed class MfaSettingsController : AdminControllerBase
{
    private readonly MfaSettingsService _settings;
    private readonly DotNetForgeDbContext _db;

    public MfaSettingsController(MfaSettingsService settings, DotNetForgeDbContext db)
    {
        _settings = settings;
        _db = db;
    }

    /// <summary>The form (bound on save) and what the screen shows around it.</summary>
    public sealed class MfaSettingsViewModel
    {
        public bool AuthenticatorApp { get; set; }
        public bool Passkeys { get; set; }
        public bool Email { get; set; }
        public bool Sms { get; set; }
        public MfaRequirement Requirement { get; set; }

        public bool EmailAvailable { get; set; }
        public bool SmsAvailable { get; set; }
        public bool CanEdit { get; set; }

        /// <summary>Accounts that set up each method, and accounts with at least one.</summary>
        public Dictionary<MfaMethodType, int> Users { get; set; } = new();
        public int UsersWithMfa { get; set; }
        public int AdminUsers { get; set; }

        public MfaSettings ToSettings() => new(AuthenticatorApp, Passkeys, Email, Sms, Requirement);
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var stored = await _settings.GetStoredAsync(HttpContext.RequestAborted);
        return View(await BuildAsync(new MfaSettingsViewModel
        {
            AuthenticatorApp = stored.AuthenticatorApp,
            Passkeys = stored.Passkeys,
            Email = stored.Email,
            Sms = stored.Sms,
            Requirement = stored.Requirement,
        }));
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Save(MfaSettingsViewModel input)
    {
        // Without SMTP or Twilio those switches are disabled and post nothing: keep the stored choice.
        var stored = await _settings.GetStoredAsync(HttpContext.RequestAborted);
        if (!await _settings.EmailAvailableAsync(HttpContext.RequestAborted))
        {
            input.Email = stored.Email;
        }

        if (!_settings.SmsAvailable)
        {
            input.Sms = stored.Sms;
        }

        // SaveAsync writes the SettingsChanged audit entry.
        var errors = await _settings.SaveAsync(input.ToSettings(), HttpContext.RequestAborted);
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                ModelState.AddModelError(string.Empty, error);
            }

            return View(nameof(Index), await BuildAsync(input));
        }

        TempData["Success"] = "Two-factor authentication settings saved.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<MfaSettingsViewModel> BuildAsync(MfaSettingsViewModel model)
    {
        var ct = HttpContext.RequestAborted;
        model.EmailAvailable = await _settings.EmailAvailableAsync(ct);
        model.SmsAvailable = _settings.SmsAvailable;
        model.CanEdit = Can(PermissionAreas.Settings, PermissionActions.Update);
        var methods = await _db.UserMfaMethods.AsNoTracking().Select(m => new { m.UserId, m.Type }).ToListAsync(ct);
        model.Users = methods.GroupBy(m => m.Type).ToDictionary(g => g.Key, g => g.Select(m => m.UserId).Distinct().Count());
        model.UsersWithMfa = methods.Select(m => m.UserId).Distinct().Count();

        // Accounts the requirement covers: those with a role that opens the admin (two single-table queries).
        var roles = await _db.Roles.AsNoTracking().Select(r => new { r.Id, r.Name }).ToListAsync(ct);
        var adminRoleIds = roles.Where(r => Roles.CanEnterAdmin(new[] { r.Name })).Select(r => r.Id).ToList();
        model.AdminUsers = await _db.UserRoles.AsNoTracking().Where(ur => adminRoleIds.Contains(ur.RoleId)).Select(ur => ur.UserId).Distinct().CountAsync(ct);
        return model;
    }
}
