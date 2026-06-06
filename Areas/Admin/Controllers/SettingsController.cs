using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Global / tenant settings (settings.md): lists key/value settings visible to the tenant (global rows
/// have a null tenant) and lets an admin add or update a tenant-scoped setting.
/// </summary>
[Route("admin/settings")]
public sealed class SettingsController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly AppEnvironment _env;
    private readonly AuditService _audit;

    public SettingsController(DotNetForgeDbContext db, AppEnvironment env, AuditService audit)
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
    public async Task<IActionResult> Save(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            ModelState.AddModelError(string.Empty, "Key is required.");
            return View(nameof(Index), await BuildAsync());
        }

        var trimmed = key.Trim();
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.TenantId == TenantId && s.Key == trimmed);
        if (setting is null)
        {
            setting = new Setting { TenantId = TenantId, Key = trimmed };
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
            .Where(s => s.TenantId == null || s.TenantId == TenantId)
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
