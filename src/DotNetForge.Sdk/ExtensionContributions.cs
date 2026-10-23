using DotNetForge.Abstractions.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetForge.Sdk;

/// <summary>
/// Plugs an extension into the host's slots from <see cref="IExtension.ConfigureServices"/>. Each helper records the
/// owning extension, so the host uses the contribution only while that extension is enabled
/// (.docs/features/extensions.md).
/// </summary>
public static class ExtensionServiceCollectionExtensions
{
    /// <summary>
    /// Offers a media storage backend (connector extensions). While the extension is enabled the host stores and serves
    /// all media through it instead of its own storage; media stored before the switch stays where it was.
    /// </summary>
    public static IServiceCollection AddMediaStorage(
        this IServiceCollection services, ExtensionContext extension, Func<IServiceProvider, IFileStorage> factory)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentNullException.ThrowIfNull(factory);
        services.AddSingleton(new MediaStorageContribution(extension.Id, factory));
        return services;
    }

    /// <summary>
    /// Adds a "Sign in with ..." button for an authentication scheme the extension registered itself (e.g. with
    /// <c>services.AddAuthentication().AddOAuth(scheme, ...)</c>) using <see cref="ExternalLogin.SignInScheme"/> as
    /// its sign-in scheme. The host signs in the existing CMS user whose email matches the scheme's
    /// <c>ClaimTypes.Email</c> claim.
    /// </summary>
    public static IServiceCollection AddLoginProvider(
        this IServiceCollection services, ExtensionContext extension, string scheme, string displayName)
    {
        ArgumentNullException.ThrowIfNull(extension);
        ArgumentException.ThrowIfNullOrWhiteSpace(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        services.AddSingleton(new LoginProviderContribution(extension.Id, scheme, displayName));
        return services;
    }

    /// <summary>
    /// Subscribes <typeparamref name="THandler"/> (scoped) to every audit entry the CMS writes. Handlers run in the
    /// background after the entry is saved; an exception is logged and never affects the request.
    /// </summary>
    public static IServiceCollection AddAuditEventHandler<THandler>(this IServiceCollection services, ExtensionContext extension)
        where THandler : class, IAuditEventHandler
    {
        ArgumentNullException.ThrowIfNull(extension);
        services.AddScoped<THandler>();
        services.AddSingleton(new AuditHandlerContribution(extension.Id, typeof(THandler)));
        return services;
    }
}

/// <summary>A media storage backend offered by an extension (see <see cref="ExtensionServiceCollectionExtensions.AddMediaStorage"/>).</summary>
public sealed record MediaStorageContribution(string ExtensionId, Func<IServiceProvider, IFileStorage> Factory);

/// <summary>A sign-in provider offered by an extension (see <see cref="ExtensionServiceCollectionExtensions.AddLoginProvider"/>).</summary>
public sealed record LoginProviderContribution(string ExtensionId, string Scheme, string DisplayName);

/// <summary>An audit event handler offered by an extension (see <see cref="ExtensionServiceCollectionExtensions.AddAuditEventHandler"/>).</summary>
public sealed record AuditHandlerContribution(string ExtensionId, Type HandlerType);
