using DotNetForge.Data;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Controllers;

/// <summary>Email/password authentication for the admin area (authentication.md, security.md).</summary>
[Route("account")]
public sealed class AccountController : Controller
{
    private readonly AuthService _authService;
    private readonly AuditService _audit;
    private readonly AppEnvironment _env;
    private readonly DotNetForgeDbContext _db;

    public AccountController(AuthService authService, AuditService audit, AppEnvironment env, DotNetForgeDbContext db)
    {
        _authService = authService;
        _audit = audit;
        _env = env;
        _db = db;
    }

    [HttpGet("login")]
    public IActionResult Login(string? returnUrl = null)
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["ReturnUrl"] = returnUrl;
        return View();
    }

    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["ReturnUrl"] = returnUrl;

        var tenant = await _db.Tenants.OrderBy(t => t.CreatedDate).FirstAsync(HttpContext.RequestAborted);
        var result = await _authService.ValidateAsync(email, password, tenant.Id, HttpContext.RequestAborted);

        switch (result.Status)
        {
            case SignInStatus.Success when result.Principal is not null:
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal);
                await _audit.LogAsync(AuditActions.UserLogin, "User", result.User?.Id.ToString(),
                    result.User?.Email);
                return LocalRedirect(string.IsNullOrEmpty(returnUrl) ? "/admin" : returnUrl);

            case SignInStatus.LockedOut:
                ModelState.AddModelError(string.Empty, "Account is temporarily locked. Try again later.");
                break;
            case SignInStatus.Disabled:
                ModelState.AddModelError(string.Empty, "This account is disabled.");
                break;
            default:
                ModelState.AddModelError(string.Empty, "Invalid email or password.");
                break;
        }

        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", null, email, success: false);
        return View();
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync(AuditActions.UserLogout);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/account/login");
    }

    [HttpGet("denied")]
    public IActionResult Denied()
    {
        ViewData["AppName"] = _env.AppName;
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }
}
