using DotNetForge.Sdk;
using Microsoft.Extensions.DependencyInjection;

namespace MarkdownKit;

/// <summary>
/// Entry point of the Markdown Kit library extension. A library has no screen of its own: it offers code to the
/// extensions that list it in their manifest "dependencies" - they reference this project at build time and share
/// this assembly at run time. It also registers <see cref="IMarkdownRenderer"/> for injection.
/// </summary>
public sealed class MarkdownKitExtension : IExtension
{
    public void ConfigureServices(IServiceCollection services, ExtensionContext context) =>
        services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
}

/// <summary>Turns Markdown into HTML that is safe to output as-is.</summary>
public interface IMarkdownRenderer
{
    string ToHtml(string? markdown);
}

/// <inheritdoc cref="Markdown"/>
public sealed class MarkdownRenderer : IMarkdownRenderer
{
    public string ToHtml(string? markdown) => Markdown.ToHtml(markdown);
}
