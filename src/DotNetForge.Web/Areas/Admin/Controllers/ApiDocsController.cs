using DotNetForge.Shared.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Controllers;

/// <summary>
/// API Docs (.docs/pages/api-docs.md): the Scalar reference of the headless API and the custom endpoints, inside the
/// admin shell. Scalar is its own full page (<c>/admin/api-docs/reference/</c>, mapped by <c>ApiDocsEndpoints</c>), so
/// this screen shows it in a frame. Super Admins and Developers only.
/// </summary>
[Route("admin/api-docs")]
[Authorize(Policy = AuthorizationPolicies.ApiDocs)]
public sealed class ApiDocsController : AdminControllerBase
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
