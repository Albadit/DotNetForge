using DotNetForge.Core.Installation;
using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Dtos;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// The server-rendered setup wizard at /setup (.docs/pages/setup.md). Creates the first Super Admin and marks the
/// CMS installed, then signs them in; reachable only while the CMS is uninstalled (the InstallationMiddleware
/// redirects here before install and blocks it afterward).
/// </summary>
[Route("setup")]
public sealed class SetupController : Controller
{
    private readonly IInstallationService _installation;
    private readonly InstallationStatusCache _status;
    private readonly AuthService _authService;
    private readonly IAuditService _audit;
    private readonly AppEnvironment _env;

    public SetupController(
        IInstallationService installation,
        InstallationStatusCache status,
        AuthService authService,
        IAuditService audit,
        AppEnvironment env)
    {
        _installation = installation;
        _status = status;
        _authService = authService;
        _audit = audit;
        _env = env;
    }

    [HttpGet("")]
    public IActionResult Index()
    {
        ViewData["AppName"] = _env.AppName;
        return View(new SetupRequest());
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
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
        var tenantId = await _authService.GetDefaultTenantIdAsync(HttpContext.RequestAborted);
        var signIn = await _authService.ValidateAsync(
            request.Email.Trim(), request.Password, tenantId, HttpContext.RequestAborted);

        if (signIn is { Status: SignInStatus.Success, Principal: not null })
        {
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, signIn.Principal);
            HttpContext.User = signIn.Principal;
        }

        await _audit.LogAsync(AuditActions.CmsInstalled, "User", signIn.User?.Id.ToString(), request.Email.Trim());
        return Redirect("/admin");
    }
}
