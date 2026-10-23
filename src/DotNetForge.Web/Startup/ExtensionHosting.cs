using System.Reflection;
using DotNetForge.Extensions;
using DotNetForge.Sdk;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.ApplicationParts;

namespace DotNetForge.Web.Startup;

/// <summary>Marks an endpoint as contributed by an extension; <see cref="ExtensionEndpointGateMiddleware"/> enforces it.</summary>
public sealed record ExtensionEndpointMetadata(string ExtensionId);

/// <summary>
/// Answers 404 for endpoints of extensions that are not active (disabled, or their code failed), so a disabled
/// extension contributes no routes (.docs/features/extensions.md#slots). Runs after routing has picked the endpoint.
/// </summary>
public sealed class ExtensionEndpointGateMiddleware
{
    private readonly RequestDelegate _next;

    public ExtensionEndpointGateMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ExtensionCatalog catalog)
    {
        var owner = context.GetEndpoint()?.Metadata.GetMetadata<ExtensionEndpointMetadata>();
        if (owner is not null && !await catalog.IsActiveAsync(owner.ExtensionId, context.RequestAborted))
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        await _next(context);
    }
}

/// <summary>
/// MVC controllers in an extension's assembly: discovered like the host's (the assembly is an application part) and
/// tagged with <see cref="ExtensionEndpointMetadata"/> so the gate switches them off with the extension.
/// </summary>
internal sealed class ExtensionAssemblyPart : ApplicationPart, IApplicationPartTypeProvider
{
    private readonly Assembly _assembly;

    public ExtensionAssemblyPart(Assembly assembly)
    {
        _assembly = assembly;
    }

    public override string Name => _assembly.GetName().Name ?? "extension";

    public IEnumerable<TypeInfo> Types => _assembly.DefinedTypes;
}

/// <inheritdoc cref="ExtensionAssemblyPart"/>
internal sealed class ExtensionControllerConvention : IControllerModelConvention
{
    private readonly ExtensionRuntime _runtime;

    public ExtensionControllerConvention(ExtensionRuntime runtime)
    {
        _runtime = runtime;
    }

    public void Apply(ControllerModel controller)
    {
        if (_runtime.FindByAssembly(controller.ControllerType.Assembly) is not { } extension)
        {
            return;
        }

        foreach (var selector in controller.Selectors)
        {
            selector.EndpointMetadata.Add(new ExtensionEndpointMetadata(extension.Id));
        }

        foreach (var action in controller.Actions)
        {
            foreach (var selector in action.Selectors)
            {
                selector.EndpointMetadata.Add(new ExtensionEndpointMetadata(extension.Id));
            }
        }
    }
}

/// <summary>Pipeline half of extension hosting (the DI half is in <see cref="DependencyRegistration"/>).</summary>
public static class ExtensionPipeline
{
    /// <summary>
    /// Maps every loaded extension's minimal-API endpoints (<c>IExtension.MapEndpoints</c>) in a group tagged with its
    /// id, and logs which extensions run. An extension whose code is not running is a warning only when it is enabled;
    /// a disabled one (e.g. a sample nobody configured) is logged as information. Runs after the database is ready.
    /// </summary>
    public static async Task MapDotNetForgeExtensionsAsync(this WebApplication app)
    {
        var runtime = app.Services.GetRequiredService<ExtensionRuntime>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DotNetForge.Extensions");

        foreach (var extension in runtime.Extensions.Where(e => e.IsLoaded))
        {
            var group = app.MapGroup(string.Empty).WithMetadata(new ExtensionEndpointMetadata(extension.Id));
            try
            {
                extension.Instance!.MapEndpoints(group, extension.Context);
            }
            catch (Exception ex)
            {
                // Extension code: record the failure (Plugins screen) instead of failing startup.
                extension.Fail($"MapEndpoints failed: {ex.Message}");
            }
        }

        var enabled = await app.Services.GetRequiredService<IExtensionState>().GetEnabledAsync(app.Lifetime.ApplicationStopping);
        foreach (var extension in runtime.Extensions)
        {
            if (extension.IsLoaded)
            {
                logger.LogInformation("Extension {ExtensionId} {Version} loaded from {Assembly}.",
                    extension.Id, extension.Context.Version, extension.Manifest.Assembly);
            }
            else if (enabled.Contains(extension.Id))
            {
                logger.LogWarning("Extension {ExtensionId} is enabled but not running: {Error}", extension.Id, extension.Error);
            }
            else
            {
                logger.LogInformation("Extension {ExtensionId} (disabled) can't run yet: {Error}", extension.Id, extension.Error);
            }
        }
    }
}
