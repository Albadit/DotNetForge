using System.Text.Json;
using DotNetForge.Data;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services.Mfa;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// Profile → Sign-in security (.docs/pages/sign-in-security.md): every admin user sets up and removes their own second
/// factors - authenticator app, passkeys, email codes, SMS codes - and their recovery codes, as far as Two-factor
/// authentication allows them. Reachable even while two-factor authentication is required and the user has none yet
/// (<see cref="AdminControllerBase"/> sends them here). The rules live in <see cref="MfaService"/>.
/// </summary>
[Route("admin/profile/security")]
public sealed class SecurityController : AdminControllerBase
{
    private const string RecoveryCodesKey = "dnf.recovery-codes";

    private readonly DotNetForgeDbContext _db;
    private readonly MfaService _mfa;
    private readonly MfaSettingsService _settings;

    public SecurityController(DotNetForgeDbContext db, MfaService mfa, MfaSettingsService settings)
    {
        _db = db;
        _mfa = mfa;
        _settings = settings;
    }

    public sealed record SecurityViewModel(User User, IReadOnlyList<UserMfaMethod> Methods, MfaSettings Settings, int RecoveryCodesLeft, bool Required);

    /// <summary>A code sent for setting up email or SMS codes, waiting to be entered.</summary>
    public sealed record CodeSetupViewModel(MfaMethodType Type, string Target, string Token);

    public sealed class PasskeyInput
    {
        public string? Token { get; set; }
        public string? Name { get; set; }
        public JsonElement Credential { get; set; }
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        if (await CurrentAsync() is not { } user)
        {
            return NotFound();
        }

        var settings = await _settings.GetAsync(HttpContext.RequestAborted);
        return View(new SecurityViewModel(user, await _mfa.MethodsAsync(user.Id, HttpContext.RequestAborted), settings,
            await _mfa.RecoveryCodesLeftAsync(user.Id, HttpContext.RequestAborted), settings.Requirement == MfaRequirement.AdminUsers));
    }

    // ---- Authenticator app ------------------------------------------------------------------------------------------

    [HttpGet("authenticator")]
    public async Task<IActionResult> Authenticator()
    {
        if (await CurrentAsync() is not { } user || !(await _settings.GetAsync(HttpContext.RequestAborted)).AuthenticatorApp)
        {
            return NotFound();
        }

        return View(_mfa.BeginAuthenticator(user));
    }

    [HttpPost("authenticator")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Authenticator(string? token, string? code)
    {
        if (await CurrentAsync() is not { } user || !(await _settings.GetAsync(HttpContext.RequestAborted)).AuthenticatorApp)
        {
            return NotFound();
        }

        // CompleteAuthenticatorAsync writes the user.mfa.added audit entry.
        if (await _mfa.CompleteAuthenticatorAsync(user, token, code, HttpContext.RequestAborted) is { } error)
        {
            ModelState.AddModelError(string.Empty, error);
            // The same secret again (it is in the signed token), so the app the user already scanned keeps working.
            return View(_mfa.RebuildAuthenticator(user, token) ?? _mfa.BeginAuthenticator(user));
        }

        return await AddedAsync(user, "Your authenticator app is set up.");
    }

    // ---- Email and SMS codes ----------------------------------------------------------------------------------------

    [HttpGet("sms")]
    public async Task<IActionResult> Sms()
    {
        if (await CurrentAsync() is null || !(await _settings.GetAsync(HttpContext.RequestAborted)).Sms)
        {
            return NotFound();
        }

        return View();
    }

    /// <summary>Sends the setup code: to the account's email, or to the phone number entered on the SMS form.</summary>
    [HttpPost("code/send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendCode(MfaMethodType type, string? phone)
    {
        if (await CurrentAsync() is not { } user || type is not (MfaMethodType.Email or MfaMethodType.Sms)
            || !(await _settings.GetAsync(HttpContext.RequestAborted)).Allows(type))
        {
            return NotFound();
        }

        var (token, error) = await _mfa.SendSetupCodeAsync(user, type, phone, HttpContext.RequestAborted);
        if (token is null)
        {
            if (type == MfaMethodType.Sms)
            {
                ModelState.AddModelError(string.Empty, error!);
                ViewData["Phone"] = phone;
                return View(nameof(Sms));
            }

            TempData["Error"] = error;
            return RedirectToAction(nameof(Index));
        }

        var target = type == MfaMethodType.Sms ? MfaService.NormalizePhone(phone)! : user.Email;
        return View("Code", new CodeSetupViewModel(type, target, token));
    }

    [HttpPost("code")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Code(MfaMethodType type, string? target, string? token, string? code)
    {
        if (await CurrentAsync() is not { } user || !(await _settings.GetAsync(HttpContext.RequestAborted)).Allows(type))
        {
            return NotFound();
        }

        // CompleteSetupCodeAsync writes the user.mfa.added audit entry.
        if (await _mfa.CompleteSetupCodeAsync(user, token, code, HttpContext.RequestAborted) is { } error)
        {
            ModelState.AddModelError(string.Empty, error);
            return View("Code", new CodeSetupViewModel(type, target ?? string.Empty, token ?? string.Empty));
        }

        return await AddedAsync(user, type == MfaMethodType.Sms ? "SMS codes are on." : "Email codes are on.");
    }

    // ---- Passkeys (passkeys.js) -------------------------------------------------------------------------------------

    [HttpPost("passkey/options")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PasskeyOptions()
    {
        if (await CurrentAsync() is not { } user || !(await _settings.GetAsync(HttpContext.RequestAborted)).Passkeys)
        {
            return Json(new { error = "Passkeys are turned off." });
        }

        var ceremony = await _mfa.BeginPasskeyRegistrationAsync(user, HttpContext.RequestAborted);
        return Json(new { options = ceremony.OptionsJson, token = ceremony.Token });
    }

    [HttpPost("passkey")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Passkey([FromBody] PasskeyInput input)
    {
        if (await CurrentAsync() is not { } user || !(await _settings.GetAsync(HttpContext.RequestAborted)).Passkeys)
        {
            return Json(new { error = "Passkeys are turned off." });
        }

        // CompletePasskeyRegistrationAsync writes the user.mfa.added audit entry.
        if (await _mfa.CompletePasskeyRegistrationAsync(user, input.Token, input.Credential.GetRawText(), input.Name, HttpContext.RequestAborted) is { } error)
        {
            return Json(new { error });
        }

        var next = await AddedAsync(user, "Your passkey is added.");
        return Json(new { redirect = ((RedirectToActionResult)next).ActionName == nameof(RecoveryCodes) ? "/admin/profile/security/recovery-codes" : "/admin/profile/security" });
    }

    // ---- Recovery codes and removing --------------------------------------------------------------------------------

    [HttpPost("recovery-codes")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> NewRecoveryCodes()
    {
        if (await CurrentAsync() is not { } user || !await _db.UserMfaMethods.AnyAsync(m => m.UserId == user.Id))
        {
            return RedirectToAction(nameof(Index));
        }

        // GenerateRecoveryCodesAsync writes the user.mfa.recovery.generated audit entry.
        TempData[RecoveryCodesKey] = string.Join(' ', await _mfa.GenerateRecoveryCodesAsync(user, HttpContext.RequestAborted));
        return RedirectToAction(nameof(RecoveryCodes));
    }

    /// <summary>The new recovery codes - shown once, right after they were made.</summary>
    [HttpGet("recovery-codes")]
    public IActionResult RecoveryCodes()
    {
        if (TempData[RecoveryCodesKey] is not string codes)
        {
            return RedirectToAction(nameof(Index));
        }

        return View(codes.Split(' '));
    }

    [HttpPost("{id:guid}/remove")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Remove(Guid id)
    {
        if (await CurrentAsync() is not { } user)
        {
            return NotFound();
        }

        // RemoveAsync writes the user.mfa.removed audit entry.
        if (!await _mfa.RemoveAsync(user, id, HttpContext.RequestAborted))
        {
            TempData["Error"] = "That method no longer exists.";
        }
        else
        {
            TempData["Success"] = await _db.UserMfaMethods.AnyAsync(m => m.UserId == user.Id)
                ? "Removed."
                : "Removed. Two-step verification is off for your account.";
        }
        return RedirectToAction(nameof(Index));
    }

    private Task<User?> CurrentAsync()
    {
        var id = CurrentUserId ?? Guid.Empty;
        return _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, HttpContext.RequestAborted);
    }

    /// <summary>After a method was added: the first one also makes the recovery codes, shown once.</summary>
    private async Task<IActionResult> AddedAsync(User user, string message)
    {
        if (await _db.UserRecoveryCodes.AnyAsync(c => c.UserId == user.Id, HttpContext.RequestAborted))
        {
            TempData["Success"] = message;
            return RedirectToAction(nameof(Index));
        }

        TempData["Success"] = message + " Save your recovery codes now.";
        TempData[RecoveryCodesKey] = string.Join(' ', await _mfa.GenerateRecoveryCodesAsync(user, HttpContext.RequestAborted));
        return RedirectToAction(nameof(RecoveryCodes));
    }
}
