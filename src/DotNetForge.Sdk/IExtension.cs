using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetForge.Sdk;

/// <summary>
/// The entry point of an extension with code: the class named by the manifest's <c>entryPoint</c> in the assembly
/// named by its <c>assembly</c> (.docs/features/extensions.md). It needs a public parameterless constructor. The host
/// creates it once at startup, for every valid extension, enabled or not; whatever it contributes is only used
/// while the extension is enabled on the Plugins screen.
/// </summary>
/// <remarks>
/// Both members run once, at startup. Changing an extension's code therefore needs a restart; enabling or disabling it
/// does not.
/// </remarks>
public interface IExtension
{
    /// <summary>
    /// Registers the extension's services in the host container. Use the <see cref="ExtensionServiceCollectionExtensions"/>
    /// helpers to plug into host slots (media storage, sign-in providers, audit events); they record which extension a
    /// contribution belongs to so the host can switch it off while the extension is disabled.
    /// </summary>
    void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
    }

    /// <summary>
    /// Maps the extension's HTTP endpoints (minimal APIs). <paramref name="endpoints"/> is a group the host answers
    /// with 404 while the extension is disabled. Endpoints are public unless you protect them, e.g. with
    /// <c>.RequireAuthorization(AuthorizationPolicies.AdminArea)</c>.
    /// </summary>
    void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
    }
}
