namespace DotNetForge.Abstractions.Extensions;

/// <summary>
/// Base contract every extension entry point implements. Extensions compile against this stable
/// abstraction and are resolved through DI by the extension host (architecture.md, extensions.md).
/// </summary>
public interface IExtension
{
    /// <summary>Stable identifier, matching the manifest <c>id</c>.</summary>
    string Id { get; }

    /// <summary>Human-readable display name.</summary>
    string Name { get; }

    /// <summary>Called once when the extension is enabled.</summary>
    void OnEnabled() { }

    /// <summary>Called once when the extension is disabled.</summary>
    void OnDisabled() { }
}

/// <summary>A public frontend theme (themes.md). Never affects the admin area.</summary>
public interface IThemeExtension : IExtension
{
    IReadOnlyList<string> Layouts { get; }
}

/// <summary>An authentication provider plugged in via DI (authentication.md).</summary>
public interface IAuthenticationProviderExtension : IExtension
{
    string ProviderKey { get; }
}

/// <summary>A dashboard or admin widget (dashboard.md).</summary>
public interface IWidgetExtension : IExtension
{
    string Render();
}

/// <summary>A page-builder module placed on pages (content_manager.md).</summary>
public interface IModuleExtension : IExtension
{
    string Render(IReadOnlyDictionary<string, string?> config);
}

/// <summary>An external-service connector (e.g. external media storage).</summary>
public interface IConnectorExtension : IExtension { }

/// <summary>A generic provider extension.</summary>
public interface IProviderExtension : IExtension { }

/// <summary>A shared library used by other extensions.</summary>
public interface ILibraryExtension : IExtension { }

/// <summary>An admin-area extension.</summary>
public interface IAdminExtension : IExtension { }

/// <summary>A general-purpose plugin.</summary>
public interface IPluginExtension : IExtension { }
