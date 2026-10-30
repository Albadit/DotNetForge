using DotNetForge.Data;
using DotNetForge.Web.Services;
using DotNetForge.Web.Services.Mfa;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Profile (.docs/pages/profile.md): every admin user edits their own name, email and password. Opened from the
/// account menu; no grant needed beyond entering the admin. Rules live in <see cref="ProfileService"/>.
/// </summary>
[Route("admin/profile")]
public sealed class ProfileController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly ProfileService _profile;
    private readonly AuthService _auth;
    private readonly MfaService _mfa;
    private readonly UserSessionService _sessions;

    public ProfileController(DotNetForgeDbContext db, ProfileService profile, AuthService auth, MfaService mfa, UserSessionService sessions)
    {
        _db = db;
        _profile = profile;
        _auth = auth;
        _mfa = mfa;
        _sessions = sessions;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var model = await BuildAsync(details: null);
        return model is null ? NotFound() : View(model);
    }

    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Details(ProfileInput input)
    {
        var error = await _profile.UpdateDetailsAsync(CurrentUserId ?? Guid.Empty, input, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            var model = await BuildAsync(input);
            return model is null ? NotFound() : View(nameof(Index), model with { Form = "details" });
        }

        // New name/email in the cookie, so the sidebar and audit entries show them at once.
        if (await _auth.RefreshPrincipalAsync(CurrentUserId!.Value, HttpContext.RequestAborted) is { } principal)
        {
            var current = await HttpContext.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, current.Properties);
        }

        TempData["Success"] = "Your profile is saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Password(PasswordChangeInput input)
    {
        var error = await _profile.ChangePasswordAsync(CurrentUserId ?? Guid.Empty, input, HttpContext.RequestAborted);
        if (error is not null)
        {
            ModelState.AddModelError(string.Empty, error);
            var model = await BuildAsync(details: null);
            return model is null ? NotFound() : View(nameof(Index), model with { Form = "password" });
        }

        // A stolen session doesn't outlive the password; this device stays signed in.
        await _sessions.EndOthersAsync(User, HttpContext.RequestAborted);

        TempData["Success"] = input.CurrentPassword is null or ""
            ? "Your password is set. You can also sign in with your email and password now."
            : "Your password is changed. Use it the next time you sign in.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ProfileViewModel?> BuildAsync(ProfileInput? details)
    {
        var id = CurrentUserId ?? Guid.Empty;
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return null;
        }

        var roleIds = await _db.UserRoles.AsNoTracking().Where(ur => ur.UserId == id).Select(ur => ur.RoleId).ToListAsync();
        var roles = await _db.Roles.AsNoTracking().Where(r => roleIds.Contains(r.Id)).OrderBy(r => r.Name).Select(r => r.Name).ToListAsync();
        return new ProfileViewModel(
            details ?? new ProfileInput { FirstName = user.FirstName, LastName = user.LastName, Email = user.Email },
            user.DisplayName, user.Email, roles, user.CreatedDate, user.LastLoginDate, Form: null,
            HasPassword: !string.IsNullOrEmpty(user.PasswordHash), MfaMethods: (await _mfa.MethodsAsync(id, HttpContext.RequestAborted)).Count);
    }

    /// <summary>
    /// The screen; <see cref="Form"/> names the form whose errors are shown (<c>details</c> / <c>password</c>).
    /// <see cref="HasPassword"/> is false for an account made through a sign-in provider (it sets a first password).
    /// </summary>
    public sealed record ProfileViewModel(ProfileInput Details, string DisplayName, string Email, IReadOnlyList<string> Roles,
        DateTime Created, DateTime? LastLogin, string? Form, bool HasPassword, int MfaMethods);
}
