using DotNetForge.Data;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Settings (.docs/pages/settings.md): lists the key/value settings and lets a user with the Settings update grant add
/// or update one.
/// </summary>
[Route("admin/settings")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed class SettingsController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly AppEnvironment _env;
    private const int MaxKeyLength = 200;
    private const int MaxValueLength = 4000;

    /// <summary>
    /// Keys owned by screens with their own rules: editing them here would skip those rules (e.g. a Super Admin
    /// default role for sign-ups).
    /// </summary>
    private static readonly (string Prefix, string Screen)[] ManagedKeys =
    {
        ("users.", "Advanced Settings"),
        (DatabaseTransferService.KeyPrefix, "Data Management"),
    };

    private readonly IAuditService _audit;

    public SettingsController(DotNetForgeDbContext db, AppEnvironment env, IAuditService audit)
    {
        _db = db;
        _env = env;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        return View(await BuildAsync());
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Save(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            ModelState.AddModelError(string.Empty, "Key is required.");
        }
        else if (key.Trim().Length > MaxKeyLength || value?.Length > MaxValueLength)
        {
            ModelState.AddModelError(string.Empty,
                $"Keys are limited to {MaxKeyLength} characters and values to {MaxValueLength}.");
        }
        else if (ManagedKeys.FirstOrDefault(m => key.Trim().StartsWith(m.Prefix, StringComparison.OrdinalIgnoreCase)) is { Screen: { } screen })
        {
            ModelState.AddModelError(string.Empty, $"'{key.Trim()}' is managed on {screen}; change it there.");
        }

        if (!ModelState.IsValid)
        {
            return View(nameof(Index), await BuildAsync());
        }

        var trimmed = key.Trim();
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == trimmed);
        if (setting is null)
        {
            setting = new Setting { Key = trimmed };
            _db.Settings.Add(setting);
        }

        setting.Value = value;
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.SettingsChanged, "Setting", trimmed, trimmed);
        TempData["Success"] = $"Saved '{trimmed}'.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<SettingsIndexViewModel> BuildAsync()
    {
        var settings = await _db.Settings
            .AsNoTracking()
            .OrderBy(s => s.Key)
            .Select(s => new { s.Key, s.Value })
            .ToListAsync();

        return new SettingsIndexViewModel
        {
            AppName = _env.AppName,
            Settings = settings.Select(s => (s.Key, s.Value)).ToList(),
        };
    }
}
