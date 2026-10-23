using DotNetForge.Data;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>Audit Logs viewer (admin). Needs the <c>Audit logs</c> read grant.</summary>
[Route("admin/audit-logs")]
[RequirePermission(PermissionAreas.AuditLogs, PermissionActions.Read)]
public sealed class AuditLogsController : AdminControllerBase
{
    private readonly DotNetForgeDbContext _db;

    public AuditLogsController(DotNetForgeDbContext db) => _db = db;

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        // Entries without a tenant (failed sign-ins: no account was resolved) belong to the sign-in tenant and stay
        // visible; entries of other tenants never are.
        var entries = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.TenantId == TenantId || a.TenantId == null)
            .OrderByDescending(a => a.Id)
            .Take(100)
            .ToListAsync();

        return View(entries);
    }
}
