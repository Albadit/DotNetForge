using DotNetForge.Core.Installation;
using DotNetForge.Data;
using DotNetForge.Shared.Dtos;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Controllers;

/// <summary>
/// Anonymous JSON API behind the React setup wizard (installation_setup.md). Reachable only while the
/// CMS is not installed (the InstallationMiddleware blocks /setup* afterward). Writes are antiforgery
/// protected via the X-CSRF-TOKEN header; the GET endpoint issues that token.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("setup")]
[Produces("application/json")]
public sealed class SetupApiController : ControllerBase
{
    private readonly IInstallationService _installation;
    private readonly InstallationStatusCache _status;
    private readonly AuthService _authService;
    private readonly DotNetForgeDbContext _db;
    private readonly IAntiforgery _antiforgery;

    public SetupApiController(
        IInstallationService installation,
        InstallationStatusCache status,
        AuthService authService,
        DotNetForgeDbContext db,
        IAntiforgery antiforgery)
    {
        _installation = installation;
        _status = status;
        _authService = authService;
        _db = db;
        _antiforgery = antiforgery;
    }

    /// <summary>Issues the antiforgery token (and cookie) for the setup form to use on submit.</summary>
    [HttpGet("antiforgery")]
    public IActionResult Antiforgery()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    /// <summary>Creates the first Super Admin, marks the CMS installed, and signs the user in.</summary>
    [HttpPost("")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Install([FromBody] SetupRequest request)
    {
        if (await _installation.IsInstalledAsync(HttpContext.RequestAborted))
        {
            return Conflict(new { error = "The CMS is already installed." });
        }

        var result = await _installation.InstallAsync(request, HttpContext.RequestAborted);
        if (result.Failed)
        {
            return BadRequest(new { error = result.Error });
        }

        _status.MarkInstalled();

        var tenant = await _db.Tenants.OrderBy(t => t.CreatedDate).FirstAsync(HttpContext.RequestAborted);
        var signIn = await _authService.ValidateAsync(
            request.Email.Trim(), request.Password, tenant.Id, HttpContext.RequestAborted);

        if (signIn is { Status: SignInStatus.Success, Principal: not null })
        {
            await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, signIn.Principal);
        }

        return Ok(new { success = true, redirect = "/admin" });
    }
}
