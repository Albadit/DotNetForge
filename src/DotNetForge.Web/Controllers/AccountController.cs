using DotNetForge.Shared.Auditing;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// Sign-in and self-service accounts (.docs/features/authentication.md, .docs/features/security.md): <c>/login</c>,
/// <c>/register</c> (when sign-ups are on), <c>/forgot-password</c> and <c>/reset-password</c> (when email is
/// configured), <c>/confirm-email</c>; sign-out and access denied stay under <c>/account</c>. The old
/// <c>/account/login</c> redirects to <c>/login</c>.
/// </summary>
[Route("account")]
public sealed class AccountController : Controller
{
    /// <summary>How long "Remember me" keeps a user signed in (sliding, like the 8-hour session without it).</summary>
    public static readonly TimeSpan RememberMeLifetime = TimeSpan.FromDays(30);

    private readonly AuthService _authService;
    private readonly AccountService _accounts;
    private readonly UserAccountSettingsService _settings;
    private readonly IAuditService _audit;
    private readonly AppEnvironment _env;
    private readonly LoginProviders _loginProviders;

    public AccountController(AuthService authService, AccountService accounts, UserAccountSettingsService settings,
        IAuditService audit, AppEnvironment env, LoginProviders loginProviders)
    {
        _authService = authService;
        _accounts = accounts;
        _settings = settings;
        _audit = audit;
        _env = env;
        _loginProviders = loginProviders;
    }

    /// <summary>The sign-in page's old address, kept for bookmarks and links.</summary>
    [HttpGet("login")]
    public IActionResult LegacyLogin() => RedirectPermanent("/login" + Request.QueryString);

    [HttpGet("/login")]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        await PrepareLoginAsync();
        return View();
    }

    [HttpPost("/login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> Login(string email, string password, bool rememberMe = false, string? returnUrl = null)
    {
        ViewData["ReturnUrl"] = returnUrl;
        ViewData["Email"] = email;
        ViewData["RememberMe"] = rememberMe;

        var result = await _authService.ValidateAsync(email, password, HttpContext.RequestAborted);

        switch (result.Status)
        {
            case SignInStatus.Success when result.Principal is not null:
                // Remember me: a persistent cookie that outlives the browser session, valid for RememberMeLifetime
                // (renewed while in use). Without it the cookie ends with the browser session or after 8 hours.
                var properties = rememberMe
                    ? new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.Add(RememberMeLifetime) }
                    : new AuthenticationProperties();
                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, result.Principal, properties);

                // SignInAsync only writes the cookie; make this request's audit entry carry the signed-in user.
                HttpContext.User = result.Principal;
                await _audit.LogAsync(AuditActions.UserLogin, "User", result.User?.Id.ToString(),
                    result.User?.Email);

                // Only redirect to local URLs; anything else goes to the admin, or to the site for site-only roles.
                return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl! : Home(result.Principal));

            // Shown as an error toast (_Feedback reads TempData in this same request).
            case SignInStatus.LockedOut:
                TempData["Error"] = "Account is temporarily locked. Try again later.";
                break;
            case SignInStatus.Disabled:
                TempData["Error"] = "This account is disabled.";
                break;
            case SignInStatus.EmailNotConfirmed:
                TempData["Error"] = "Confirm your email first: open the link we sent you.";
                ViewData["Unconfirmed"] = true;
                break;
            default:
                TempData["Error"] = "Invalid email or password.";
                break;
        }

        await _audit.LogAsync(AuditActions.UserLoginFailed, "User", null, email, success: false);
        await PrepareLoginAsync();
        return View();
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _audit.LogAsync(AuditActions.UserLogout);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/login");
    }

    // ---- Registration (Advanced User Settings: "Enable sign-ups", off by default) --------------------------------

    [HttpGet("/register")]
    public async Task<IActionResult> Register()
    {
        if (!(await _settings.GetAsync(HttpContext.RequestAborted)).SignUps)
        {
            return NotFound();
        }

        ViewData["AppName"] = _env.AppName;
        return View(new RegisterInput());
    }

    [HttpPost("/register")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> Register(RegisterInput input)
    {
        ViewData["AppName"] = _env.AppName;
        var result = await _accounts.RegisterAsync(input, HttpContext.RequestAborted);
        if (result.User is null)
        {
            if (!(await _settings.GetAsync(HttpContext.RequestAborted)).SignUps)
            {
                return NotFound();
            }

            ModelState.AddModelError(string.Empty, result.Error!);
            input.Password = input.ConfirmPassword = null;
            return View(input);
        }

        if (result.MustConfirm)
        {
            return Notice("Check your email",
                $"We sent a confirmation link to {result.User.Email}. Open it to finish creating your account, then sign in.");
        }

        // No confirmation needed: sign the new account in right away.
        var signIn = await _authService.ValidateAsync(input.Email!, input.Password!, HttpContext.RequestAborted);
        if (signIn is { Status: SignInStatus.Success, Principal: not null })
        {
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, signIn.Principal);
            var home = Home(signIn.Principal);
            if (home == "/admin")
            {
                // The public site shows no toasts; a TempData message would wait for the next admin screen.
                TempData["Success"] = "Welcome! Your account is ready.";
            }

            return LocalRedirect(home);
        }

        return Redirect("/login");
    }

    // ---- Password reset (needs SMTP) ------------------------------------------------------------------------------

    [HttpGet("/forgot-password")]
    public async Task<IActionResult> ForgotPassword()
    {
        if (!await _accounts.EmailEnabledAsync(HttpContext.RequestAborted))
        {
            return NotFound();
        }

        ViewData["AppName"] = _env.AppName;
        return View();
    }

    [HttpPost("/forgot-password")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> ForgotPassword(string? email)
    {
        if (!await _accounts.EmailEnabledAsync(HttpContext.RequestAborted))
        {
            return NotFound();
        }

        await _accounts.RequestPasswordResetAsync(email, HttpContext.RequestAborted);
        // The same answer whether or not the account exists (no enumeration).
        return Notice("Check your email",
            $"If an account uses {email?.Trim()}, we sent it a link to choose a new password. The link works for {AccountService.ResetLinkLifetime.TotalMinutes:0} minutes.");
    }

    [HttpGet("/reset-password")]
    public async Task<IActionResult> ResetPassword(string? token)
    {
        ViewData["AppName"] = _env.AppName;
        if (await _accounts.FindResetUserAsync(token, HttpContext.RequestAborted) is null)
        {
            return InvalidLink();
        }

        ViewData["Token"] = token;
        return View();
    }

    // No antiforgery token: the single-use reset token in the form is the proof, so a front end's own reset page
    // (Advanced User Settings: "Reset password page") can post its form here.
    [HttpPost("/reset-password")]
    [IgnoreAntiforgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> ResetPassword(string? token, string? password, string? confirmPassword)
    {
        ViewData["AppName"] = _env.AppName;
        if (await _accounts.FindResetUserAsync(token, HttpContext.RequestAborted) is null)
        {
            return InvalidLink();
        }

        if (await _accounts.ResetPasswordAsync(token, password, confirmPassword, HttpContext.RequestAborted) is { } error)
        {
            ModelState.AddModelError(string.Empty, error);
            ViewData["Token"] = token;
            return View();
        }

        TempData["Success"] = "Your password is changed. Sign in with the new one.";
        return Redirect("/login");
    }

    // ---- Email confirmation (Advanced User Settings: "Enable email confirmation") ------------------------------------

    [HttpGet("/confirm-email")]
    public async Task<IActionResult> ConfirmEmail(string? token)
    {
        ViewData["AppName"] = _env.AppName;
        if (await _accounts.ConfirmEmailAsync(token, HttpContext.RequestAborted) is null)
        {
            return InvalidLink();
        }

        var target = await _accounts.ConfirmedRedirectAsync(HttpContext.RequestAborted);
        if (!target.StartsWith('/'))
        {
            return Redirect(target); // the configured front end's page
        }

        TempData["Success"] = "Your email is confirmed. You can sign in now.";
        return LocalRedirect(target);
    }

    [HttpPost("/confirm-email/resend")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting(RateLimitPolicies.Credentials)]
    public async Task<IActionResult> ResendConfirmation(string? email)
    {
        ViewData["AppName"] = _env.AppName;
        await _accounts.ResendConfirmationAsync(email, HttpContext.RequestAborted);
        return Notice("Check your email", $"If {email?.Trim()} still needs confirming, we sent it a new confirmation link.");
    }

    [HttpGet("denied")]
    public IActionResult Denied()
    {
        ViewData["AppName"] = _env.AppName;
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    /// <summary>The sign-in page's extras: external providers, and the register / forgot-password links when available.</summary>
    private async Task PrepareLoginAsync()
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["LoginProviders"] = await _loginProviders.GetActiveAsync(HttpContext.RequestAborted);
        ViewData["SignUps"] = (await _settings.GetAsync(HttpContext.RequestAborted)).SignUps;
        ViewData["CanResetPassword"] = await _accounts.EmailEnabledAsync(HttpContext.RequestAborted);
    }

    /// <summary>The admin for admin-capable roles, the site for everyone else (e.g. a registered "Authenticated" user).</summary>
    private static string Home(System.Security.Claims.ClaimsPrincipal user) =>
        Roles.CanEnterAdmin(user.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value)) ? "/admin" : "/";

    private ViewResult Notice(string title, string text, bool problem = false)
    {
        ViewData["AppName"] = _env.AppName;
        ViewData["Problem"] = problem;
        ViewData["Title"] = title;
        ViewData["Text"] = text;
        return View("Notice");
    }

    private ViewResult InvalidLink() =>
        Notice("This link doesn't work", "It is invalid, has expired or was already used. Ask for a new one from the sign-in page.", problem: true);
}
