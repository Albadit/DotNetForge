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
        var entries = await _db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(a => a.Id)
            .Take(100)
            .ToListAsync();

        return View(entries);
    }
}
