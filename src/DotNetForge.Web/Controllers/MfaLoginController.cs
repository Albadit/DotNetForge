using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services;
using DotNetForge.Web.Services.Mfa;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// The second step of signing in (.docs/features/mfa.md#sign-in, .docs/pages/login.md#two-factor-step): after the
/// password - or a provider - was right for an account with a second factor (<see cref="PendingSignIn"/>), the user
/// confirms with their authenticator app, a passkey, an emailed or texted code, or a recovery code. A wrong code counts
/// toward the account lockout like a wrong password. Without a pending sign-in every route goes back to <c>/login</c>.
/// </summary>
[Route("login/mfa")]
[EnableRateLimiting(RateLimitPolicies.Credentials)]
public sealed class MfaLoginController : Controller
{
    private readonly AuthService _auth;
    private readonly MfaService _mfa;
    private readonly IAuditService _audit;
    private readonly AppEnvironment _env;

    public MfaLoginController(AuthService auth, MfaService mfa, IAuditService audit, AppEnvironment env)
    {
        _auth = auth;
        _mfa = mfa;
        _audit = audit;
        _env = env;
    }

    /// <summary>What the code page shows.</summary>
    public sealed record MfaLoginViewModel(string Email, IReadOnlyList<UserMfaMethod> Methods, UserMfaMethod? Selected, bool Recovery,
        bool CodeSent, string? Via);

    [HttpGet("")]
    [DisableRateLimiting]
    public async Task<IActionResult> Index(Guid? method = null, bool recovery = false)
    {
        var (pending, user, methods) = await LoadAsync();
        if (pending is null || user is null)
        {
            return await RestartAsync();
        }

        return Show(pending, user, methods, method, recovery);
    }

    /// <summary>Checks an authenticator, email, SMS or recovery code.</summary>
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Verify(Guid? method, string? code, bool recovery = false)
    {
        var (pending, user, methods) = await LoadAsync();
        if (pending is null || user is null)
        {
            return await RestartAsync();
        }

        var selected = methods.FirstOrDefault(m => m.Id == method);
        bool ok;
        string how;
        if (recovery)
        {
            ok = await _mfa.UseRecoveryCodeAsync(user.Id, code, HttpContext.RequestAborted);
            how = "a recovery code";
        }
        else if (selected is { Type: MfaMethodType.AuthenticatorApp })
        {
            ok = await _mfa.VerifyAuthenticatorAsync(user, code, HttpContext.RequestAborted);
            how = "an authenticator app";
        }
        else if (selected is { Type: MfaMethodType.Email or MfaMethodType.Sms } && pending.CodeMethodId == selected.Id)
        {
            ok = await _mfa.VerifySignInCodeAsync(user, selected, pending.CodeToken, code, HttpContext.RequestAborted);
            how = selected.Type == MfaMethodType.Sms ? "an SMS code" : "an email code";
        }
        else
        {
            return RedirectToAction(nameof(Index));
        }

        if (ok)
        {
            var left = recovery ? await _mfa.RecoveryCodesLeftAsync(user.Id, HttpContext.RequestAborted) : -1;
            if (left is >= 0 and <= 3)
            {
                TempData["Info"] = left == 0
                    ? "That was your last recovery code. Make new ones on Profile › Sign-in security."
                    : $"{left} recovery code{(left == 1 ? "" : "s")} left. Make new ones on Profile › Sign-in security.";
            }

            return await FinishAsync(pending, how);
        }

        return await WrongAsync(pending, user, methods, selected, recovery, how);
    }

    /// <summary>Emails or texts a code for the chosen method.</summary>
    [HttpPost("send")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Send(Guid method)
    {
        var (pending, user, methods) = await LoadAsync();
        if (pending is null || user is null)
        {
            return await RestartAsync();
        }

        var selected = methods.FirstOrDefault(m => m.Id == method && m.Type is MfaMethodType.Email or MfaMethodType.Sms);
        if (selected is null)
        {
            return RedirectToAction(nameof(Index));
        }

        var token = await _mfa.SendSignInCodeAsync(user, selected, HttpContext.RequestAborted);
        if (token is null)
        {
            TempData["Error"] = selected.Type == MfaMethodType.Sms
                ? "The text message couldn't be sent. Try another way, or try again later."
                : "The email couldn't be sent. Try another way, or try again later.";
        }
        else
        {
            await (pending with { CodeMethodId = selected.Id, CodeToken = token }).SaveAsync(HttpContext);
            TempData["Success"] = selected.Type == MfaMethodType.Sms ? $"We texted a code to {selected.Name}." : "We emailed you a code.";
        }

        return RedirectToAction(nameof(Index), new { method = selected.Id });
    }

    /// <summary>The options for <c>navigator.credentials.get</c> (passkeys.js).</summary>
    [HttpPost("passkey/options")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PasskeyOptions()
    {
        var (pending, user, _) = await LoadAsync();
        if (pending is null || user is null || await _mfa.BeginPasskeySignInAsync(user, HttpContext.RequestAborted) is not { } ceremony)
        {
            return Json(new { error = "Start signing in again.", redirect = "/login" });
        }

        await (pending with { PasskeyToken = ceremony.Token }).SaveAsync(HttpContext);
        return Content(ceremony.OptionsJson, "application/json");
    }

    /// <summary>Checks the passkey's signature; answers where to go next.</summary>
    [HttpPost("passkey")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Passkey([FromBody] System.Text.Json.JsonElement credential)
    {
        var (pending, user, _) = await LoadAsync();
        if (pending is null || user is null)
        {
            return Json(new { error = "Start signing in again.", redirect = "/login" });
        }

        if (await _mfa.VerifyPasskeyAsync(user, pending.PasskeyToken, credential.GetRawText(), HttpContext.RequestAborted))
        {
            var done = await FinishAsync(pending, "a passkey");
            return Json(new { redirect = done is RedirectResult r ? r.Url : done is LocalRedirectResult l ? l.Url : "/login" });
        }

        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", user.Id.ToString(), user.Email, success: false, details: "wrong passkey");
        if (await _auth.RecordFailedSecondFactorAsync(user.Id, HttpContext.RequestAborted))
        {
            await PendingSignIn.EndAsync(HttpContext);
            TempData["Error"] = "Account is temporarily locked. Try again later.";
            return Json(new { redirect = "/login" });
        }

        return Json(new { error = "That passkey didn't work. Try again, or use another way." });
    }

    [HttpPost("cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel()
    {
        await PendingSignIn.EndAsync(HttpContext);
        return Redirect("/login");
    }

    private async Task<(PendingSignIn? Pending, User? User, List<UserMfaMethod> Methods)> LoadAsync()
    {
        var pending = await PendingSignIn.ReadAsync(HttpContext);
        var user = pending is null ? null : await _auth.FindPendingUserAsync(pending.UserId, HttpContext.RequestAborted);
        var methods = user is null ? new List<UserMfaMethod>() : await _mfa.UsableMethodsAsync(user.Id, HttpContext.RequestAborted);
        // An account whose methods were all removed meanwhile can't finish here: start again.
        return methods.Count == 0 ? (null, null, methods) : (pending, user, methods);
    }

    private IActionResult Show(PendingSignIn pending, User user, List<UserMfaMethod> methods, Guid? method, bool recovery)
    {
        var selected = recovery ? null : methods.FirstOrDefault(m => m.Id == method) ?? methods[0];
        ViewData["AppName"] = _env.AppName;
        return View("Index", new MfaLoginViewModel(user.Email, methods, selected, recovery,
            selected is not null && pending.CodeMethodId == selected.Id && pending.CodeToken is not null, pending.Via));
    }

    private async Task<IActionResult> WrongAsync(PendingSignIn pending, User user, List<UserMfaMethod> methods, UserMfaMethod? selected, bool recovery, string how)
    {
        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", user.Id.ToString(), user.Email, success: false, details: "wrong code from " + how);
        if (await _auth.RecordFailedSecondFactorAsync(user.Id, HttpContext.RequestAborted))
        {
            await PendingSignIn.EndAsync(HttpContext);
            TempData["Error"] = "Account is temporarily locked. Try again later.";
            return Redirect("/login");
        }

        ModelState.AddModelError(string.Empty, recovery ? "That recovery code is not right, or it was used already." : "That code is not right.");
        return Show(pending, user, methods, selected?.Id, recovery);
    }

    private async Task<IActionResult> FinishAsync(PendingSignIn pending, string how)
    {
        await PendingSignIn.EndAsync(HttpContext);
        var result = await _auth.CompleteSecondFactorAsync(pending.UserId, HttpContext.RequestAborted);
        if (result is not { Status: SignInStatus.Success, Principal: { } principal })
        {
            TempData["Error"] = result.Status == SignInStatus.LockedOut ? "Account is temporarily locked. Try again later." : "This account can't sign in.";
            return Redirect("/login");
        }

        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal, AccountController.SessionProperties(pending.RememberMe));
        HttpContext.User = principal;
        await _audit.LogAsync(AuditActions.UserLogin, "User", result.User?.Id.ToString(), result.User?.Email,
            details: (pending.Via is null ? "" : $"via {pending.Via}, ") + "with " + how);
        return LocalRedirect(pending.ReturnUrl ?? AccountController.Home(principal));
    }

    private async Task<IActionResult> RestartAsync()
    {
        await PendingSignIn.EndAsync(HttpContext);
        TempData["Error"] = "Your sign-in expired. Sign in again.";
        return Redirect("/login");
    }
}
