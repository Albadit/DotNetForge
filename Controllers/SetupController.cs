using DotNetForge.Core.Installation;
using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Dtos;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// The server-rendered setup wizard at /setup. Creates the first Super Admin and marks the CMS
/// installed, then signs them in; reachable only while the CMS is uninstalled (the
/// InstallationMiddleware redirects here before install and blocks it afterward).
/// </summary>
[Route("setup")]
public sealed class SetupController : Controller
{
    private readonly IInstallationService _installation;
    private readonly InstallationStatusCache _status;
    private readonly AuthService _authService;
    private readonly AppEnvironment _env;
    private readonly DotNetForgeDbContext _db;

    public SetupController(
        IInstallationService installation,
        InstallationStatusCache status,
        AuthService authService,
        AppEnvironment env,
        DotNetForgeDbContext db)
    {
        _installation = installation;
        _status = status;
        _authService = authService;
        _env = env;
        _db = db;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["AppName"] = _env.AppName;
        return View(new SetupRequest());
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(SetupRequest request)
    {
        ViewData["AppName"] = _env.AppName;

        var result = await _installation.InstallAsync(request, HttpContext.RequestAborted);
        if (result.Failed)
        {
            ModelState.AddModelError(string.Empty, result.Error);
            request.Password = string.Empty;
            request.ConfirmPassword = string.Empty;
            return View(request);
        }

        _status.MarkInstalled();

        // Sign the new Super Admin in and head to the dashboard.
        var tenant = await _db.Tenants.OrderBy(t => t.CreatedDate).FirstAsync(HttpContext.RequestAborted);
        var signIn = await _authService.ValidateAsync(
            request.Email.Trim(), request.Password, tenant.Id, HttpContext.RequestAborted);

        if (signIn is { Status: SignInStatus.Success, Principal: not null })
        {
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, signIn.Principal);
        }

        return Redirect("/admin");
    }
}
