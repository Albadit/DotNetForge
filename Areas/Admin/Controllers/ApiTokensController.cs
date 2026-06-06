using DotNetForge.Abstractions.Security;
using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Areas.Admin.Models;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// API token management (admin). Tokens are shown in plaintext exactly once at creation; only a salted
/// hash is stored. Restricted to Super Admin / Admin.
/// </summary>
[Route("admin/api-tokens")]
[Authorize(Roles = $"{Roles.SuperAdmin},{Roles.Admin}")]
public sealed class ApiTokensController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;
    private readonly IApiTokenFactory _tokenFactory;
    private readonly IDateTimeProvider _clock;
    private readonly AuditService _audit;

    public ApiTokensController(
        DotNetForgeDbContext db, IApiTokenFactory tokenFactory, IDateTimeProvider clock, AuditService audit)
    {
        _db = db;
        _tokenFactory = tokenFactory;
        _clock = clock;
        _audit = audit;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var tokens = await _db.ApiTokens
            .AsNoTracking()
            .Where(t => t.TenantId == TenantId)
            .OrderByDescending(t => t.CreatedDate)
            .ToListAsync();

        return View(tokens);
    }

    [HttpGet("create")]
    public IActionResult Create()
    {
        ViewBag.Permissions = PermissionKeys.All;
        return View();
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string name, string? description, string duration, string[]? permissions)
    {
        ViewBag.Permissions = PermissionKeys.All;

        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError(string.Empty, "Name is required.");
            return View();
        }

        var selected = (permissions ?? Array.Empty<string>())
            .Where(PermissionKeys.IsKnown)
            .Distinct()
            .ToArray();

        if (selected.Length == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one permission.");
            return View();
        }

        var tokenDuration = ParseDuration(duration);
        var generated = _tokenFactory.Generate();

        var token = new ApiToken
        {
            TenantId = TenantId,
            Name = name.Trim(),
            Description = description,
            Duration = tokenDuration,
            ExpirationDate = ComputeExpiration(tokenDuration),
            PermissionsCsv = string.Join(',', selected),
            CreatedById = CurrentUserId ?? Guid.Empty,
            CreatedDate = _clock.UtcNow,
            TokenHash = generated.Hash,
            TokenPrefix = generated.Prefix,
        };

        _db.ApiTokens.Add(token);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(AuditActions.ApiTokenCreated, "ApiToken", token.Id.ToString(), token.Name);

        return View("Created", new CreatedTokenViewModel
        {
            Name = token.Name,
            Plaintext = generated.Plaintext,
            Prefix = generated.Prefix,
        });
    }

    [HttpPost("revoke/{id:guid}")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Revoke(Guid id)
    {
        var token = await _db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id && t.TenantId == TenantId);
        if (token is not null && !token.Revoked)
        {
            token.Revoked = true;
            await _db.SaveChangesAsync();
            await _audit.LogAsync(AuditActions.ApiTokenRevoked, "ApiToken", token.Id.ToString(), token.Name);
        }

        return RedirectToAction(nameof(Index));
    }

    private DateTime? ComputeExpiration(TokenDuration duration) => duration switch
    {
        TokenDuration.SevenDays => _clock.UtcNow.AddDays(7),
        TokenDuration.ThirtyDays => _clock.UtcNow.AddDays(30),
        TokenDuration.NinetyDays => _clock.UtcNow.AddDays(90),
        TokenDuration.Unlimited => null,
        _ => _clock.UtcNow.AddDays(30),
    };

    private static TokenDuration ParseDuration(string? duration) => duration switch
    {
        "7" => TokenDuration.SevenDays,
        "30" => TokenDuration.ThirtyDays,
        "90" => TokenDuration.NinetyDays,
        "unlimited" => TokenDuration.Unlimited,
        _ => TokenDuration.ThirtyDays,
    };
}
