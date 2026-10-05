using DotNetForge.Core.Extensions;
using Microsoft.AspNetCore.Mvc;

namespace DotNetForge.Web.Areas.Admin.Components;

/// <summary>
/// Renders sidebar entries for the valid <c>admin</c>-type extensions discovered under <c>extensions/admin/</c>
/// (.docs/features/extensions.md). Dropping a manifest there adds a tab at <c>/admin/ext/{id}</c> with no code
/// changes - the server-rendered equivalent of the old SPA's dynamic admin tabs.
/// </summary>
public sealed class AdminExtensionsNavViewComponent : ViewComponent
{
    private readonly IExtensionLoader _loader;

    public AdminExtensionsNavViewComponent(IExtensionLoader loader)
    {
        _loader = loader;
    }

    public IViewComponentResult Invoke()
    {
        var items = _loader.Discover()
            .Where(d => d.IsValid && d.Manifest is not null &&
                        string.Equals(d.Manifest.Type, "admin", StringComparison.OrdinalIgnoreCase))
            .Select(d => (Id: d.Manifest!.Id, Name: d.Manifest.Name))
            .OrderBy(x => x.Name)
            .ToList();

        return View(items);
    }
}
