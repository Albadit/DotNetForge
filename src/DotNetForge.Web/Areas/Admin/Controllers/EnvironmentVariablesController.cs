using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Settings → Global Settings → Environment Variables (.docs/pages/environment-variables.md): the site's key/value
/// settings as secret or config variables, for all environments or one. Reading needs Settings read, adding, changing
/// and deleting Settings update; the rules live in <see cref="EnvironmentVariableService"/>. The old Settings URLs
/// (<c>/admin/settings</c>) lead here.
/// </summary>
[Route("admin/environment-variables")]
[RequirePermission(PermissionAreas.Settings, PermissionActions.Read)]
public sealed class EnvironmentVariablesController : AdminControllerBase
{
    private const string Screen = "/admin/environment-variables";

    private readonly EnvironmentVariableService _variables;
    private readonly AppEnvironment _env;

    public EnvironmentVariablesController(EnvironmentVariableService variables, AppEnvironment env)
    {
        _variables = variables;
        _env = env;
    }

    /// <summary>The list; <see cref="Sort"/> is <c>updated</c> (newest first, the default) or <c>name</c>.</summary>
    public sealed record EnvironmentVariablesViewModel(
        IReadOnlyList<EnvironmentVariableRow> Variables, bool CanEdit, string Sort, string AppName, string RunningEnvironment);

    /// <summary>The add/edit form.</summary>
    public sealed record EnvironmentVariableFormModel(EnvironmentVariableInput Input, bool IsNew, bool WasSecret);

    [HttpGet("")]
    public async Task<IActionResult> Index(string? sort)
    {
        var rows = await _variables.ListAsync(HttpContext.RequestAborted);
        sort = sort == "name" ? "name" : "updated";
        var ordered = sort == "name"
            ? rows.OrderBy(r => r.Key, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.Environment).ToList()
            : rows.OrderByDescending(r => r.UpdatedDate).ToList();
        return View(new EnvironmentVariablesViewModel(ordered, Can(PermissionAreas.Settings, PermissionActions.Update), sort, _env.AppName,
            DotNetForge.Shared.Entities.SettingEnvironment.Current));
    }

    [HttpGet("new")]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public IActionResult New() => View("Form", new EnvironmentVariableFormModel(new EnvironmentVariableInput { IsSecret = true }, IsNew: true, WasSecret: false));

    [HttpGet("{id:guid}")]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Edit(Guid id)
    {
        var input = await _variables.FindAsync(id, HttpContext.RequestAborted);
        if (input is null)
        {
            return NotFound();
        }

        // A key another screen owns is changed there.
        if (EnvironmentVariableService.ManagedBy(input.Key!) is { } managed)
        {
            return Redirect(managed.Href);
        }

        return View("Form", new EnvironmentVariableFormModel(input, IsNew: false, WasSecret: input.IsSecret));
    }

    [HttpPost("save")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Save(EnvironmentVariableInput input)
    {
        var error = await _variables.SaveAsync(input, CurrentUserId, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            var wasSecret = input.Id is { } id && (await _variables.FindAsync(id, HttpContext.RequestAborted))?.IsSecret == true;
            // A secret's value is never sent back to the browser, not even the one just typed.
            input.Value = input.IsSecret ? null : input.Value;
            return View("Form", new EnvironmentVariableFormModel(input, IsNew: input.Id is null, wasSecret));
        }

        TempData["Success"] = T["Saved '{0}'.", input.Key?.Trim() ?? string.Empty];
        return Redirect(Screen);
    }

    [HttpPost("{id:guid}/delete")]
    [ValidateAntiForgeryToken]
    [RequirePermission(PermissionAreas.Settings, PermissionActions.Update)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var error = await _variables.DeleteAsync(id, HttpContext.RequestAborted);
        if (error is null)
        {
            TempData["Success"] = T["Environment variable deleted."];
        }
        else
        {
            TempData["Error"] = error;
        }

        return Redirect(Screen);
    }

    /// <summary>The screen was called Settings: its old addresses lead here.</summary>
    [HttpGet("/admin/settings")]
    [HttpGet("/admin/settings/new")]
    [HttpGet("/admin/settings/edit")]
    public IActionResult Moved() => RedirectPermanent(Request.Path.Value!.EndsWith("/new", StringComparison.Ordinal) ? Screen + "/new" : Screen);
}
