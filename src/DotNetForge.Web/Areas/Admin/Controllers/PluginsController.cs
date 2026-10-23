using DotNetForge.Core.Extensions;
using DotNetForge.Data;
using DotNetForge.Extensions;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Plugins screen (.docs/pages/plugins.md): every extension found under <c>extensions/</c> with its manifest
/// validity, whether its code is running, and its status, plus Enable / Disable. Restricted to Super Admin.
/// </summary>
[Route("admin/plugins")]
[Authorize(Roles = Roles.SuperAdmin)]
public sealed class PluginsController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly IExtensionLoader _loader;
    private readonly ExtensionCatalog _catalog;
    private readonly ExtensionLifecycleService _lifecycle;
    private readonly AppEnvironment _env;

    public PluginsController(DotNetForgeDbContext db, IExtensionLoader loader, ExtensionCatalog catalog,
        ExtensionLifecycleService lifecycle, AppEnvironment env)
    {
        _db = db;
        _loader = loader;
        _catalog = catalog;
        _lifecycle = lifecycle;
        _env = env;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var records = await _db.InstalledExtensions.AsNoTracking()
            .Select(x => new { x.Id, x.Status, x.InstalledDate })
            .ToListAsync(HttpContext.RequestAborted);
        var byId = records.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        var snapshot = _loader.Discover();
        var installed = ExtensionCatalog.FirstValidPerId(snapshot).ToHashSet();

        var rows = new List<PluginRowViewModel>();
        foreach (var discovered in snapshot)
        {
            var m = discovered.Manifest;
            var record = m?.Id is null ? null : byId.GetValueOrDefault(m.Id);
            var folder = Path.GetDirectoryName(discovered.Path) ?? discovered.Path;
            var problem = !discovered.IsValid
                ? string.Join(" ", discovered.Validation.Errors.Select(e => e.ToString()))
                : !installed.Contains(discovered)
                    ? "Another extension already uses this id; only the first one found is used."
                    : _catalog.Problem(discovered);

            rows.Add(new PluginRowViewModel
            {
                Id = m?.Id ?? Path.GetFileName(folder),
                Name = m?.Name ?? "(unknown)",
                Description = m?.Description ?? string.Empty,
                Version = m?.Version ?? "-",
                Type = m?.Type?.ToLowerInvariant() ?? "-",
                Status = record?.Status == ExtensionStatus.Enabled ? "Enabled" : "Disabled",
                Author = m?.Author ?? "-",
                ValidManifest = discovered.IsValid,
                HasCode = m?.Assembly is not null,
                Dependencies = m?.Dependencies ?? new List<string>(),
                Problem = problem,
                Folder = Path.GetRelativePath(_env.ExtensionsPath, folder).Replace('\\', '/'),
                InstalledDate = record?.InstalledDate,
                CanToggle = discovered.IsValid && installed.Contains(discovered),
            });
        }

        return View(rows.OrderBy(r => r.Type).ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList());
    }

    [HttpPost("{id}/enable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Enable(string id) =>
        Done(await _lifecycle.EnableAsync(id, HttpContext.RequestAborted), "enabled");

    [HttpPost("{id}/disable")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disable(string id) =>
        Done(await _lifecycle.DisableAsync(id, HttpContext.RequestAborted), "disabled");

    private IActionResult Done(string? error, string verb)
    {
        if (error is null)
        {
            TempData["Success"] = $"Extension {verb}.";
        }
        else
        {
            TempData["Error"] = error;
        }

        return RedirectToAction(nameof(Index));
    }
}
