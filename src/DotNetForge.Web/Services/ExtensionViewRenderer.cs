using System.Text.Encodings.Web;
using DotNetForge.Extensions;
using DotNetForge.Shared.Configuration;
using DotNetForge.Web.Startup;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace DotNetForge.Web.Services;

/// <summary>
/// Renders an extension's Razor view (compiled in memory by runtime compilation) into a host screen - widgets on the
/// dashboard, modules on content pages (.docs/features/extensions.md#slots). The view gets a fresh ViewData with the
/// extension's <c>Extension</c>, <c>Settings</c> and <c>ResourceBase</c>; if it throws, the error is logged and a
/// short notice is rendered instead, so one broken extension never breaks the screen around it.
/// </summary>
public sealed class ExtensionViewRenderer
{
    private readonly IRazorViewEngine _engine;
    private readonly ITempDataProvider _tempData;
    private readonly ILogger<ExtensionViewRenderer> _logger;
    private readonly string _viewRoot;

    public ExtensionViewRenderer(IRazorViewEngine engine, ITempDataProvider tempData, AppEnvironment env,
        ILogger<ExtensionViewRenderer> logger)
    {
        _engine = engine;
        _tempData = tempData;
        _logger = logger;
        _viewRoot = DependencyRegistration.ExtensionViewRoot(env);
    }

    /// <summary>The app-relative view path (<c>~/extensions/...</c>) the runtime view engine resolves for a file.</summary>
    public string ToViewPath(string fullPath) => "~/" + Path.GetRelativePath(_viewRoot, fullPath).Replace('\\', '/');

    /// <summary>Puts the values every extension view receives into <paramref name="viewData"/>.</summary>
    public static void AddExtensionViewData(ViewDataDictionary viewData, ActiveExtension extension, bool admin)
    {
        viewData["Extension"] = extension.Context;
        viewData["Settings"] = extension.Context.Settings.ToDictionary(kv => kv.Key, kv => kv.Value);
        viewData["ResourceBase"] = admin ? extension.Context.AdminResourceBase : extension.Context.PublicResourceBase;
    }

    /// <summary>Renders <paramref name="viewFile"/> (a full path inside the extension) as a partial.</summary>
    public async Task<IHtmlContent> RenderAsync(HttpContext http, ActiveExtension extension, string viewFile, object? model, bool admin)
    {
        var viewPath = ToViewPath(viewFile);
        try
        {
            var found = _engine.GetView(executingFilePath: null, viewPath, isMainPage: false);
            if (!found.Success)
            {
                throw new InvalidOperationException($"View '{viewPath}' was not found.");
            }

            var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
            AddExtensionViewData(viewData, extension, admin);

            await using var writer = new StringWriter();
            var actionContext = new ActionContext(http, http.GetRouteData(), new ActionDescriptor());
            var viewContext = new ViewContext(actionContext, found.View, viewData, new TempDataDictionary(http, _tempData),
                writer, new HtmlHelperOptions());
            await found.View.RenderAsync(viewContext);
            return new HtmlString(writer.ToString());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Extension views are foreign code: log the cause for the operator, show a neutral notice to the visitor.
            _logger.LogError(ex, "Extension {ExtensionId} failed to render {View}.", extension.Id, viewPath);
            return new HtmlString(
                $"<div class=\"dnf-ext-error\">{HtmlEncoder.Default.Encode(extension.Context.Name)} could not be displayed.</div>");
        }
    }
}
