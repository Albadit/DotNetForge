using System.Text;
using System.Xml;
using DotNetForge.Sdk;
using DotNetForge.Shared.Configuration;
using DotNetForge.Shared.Content;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SeoToolkit;

/// <summary>
/// Entry point of the SEO Toolkit plugin: <c>/sitemap.xml</c> (every live page, from the host's
/// <see cref="IPublishedPages"/>) and <c>/robots.txt</c>. Absolute URLs use the site's <c>APP_URL</c>. Both endpoints
/// disappear (404) while the plugin is disabled.
/// </summary>
public sealed class SeoToolkitPlugin : IExtension
{
    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        if (context.GetBool("generateSitemap", true))
        {
            endpoints.MapGet("/sitemap.xml", async (IPublishedPages pages, AppEnvironment env, CancellationToken ct) =>
                Results.Text(Sitemap(await pages.ListAsync(ct), BaseUrl(env)), "application/xml", Encoding.UTF8));
        }

        endpoints.MapGet("/robots.txt", (AppEnvironment env) =>
            Results.Text(Robots(context, BaseUrl(env)), "text/plain", Encoding.UTF8));
    }

    public static string Sitemap(IEnumerable<PublishedPage> pages, string baseUrl)
    {
        var xml = new StringBuilder();
        using (var writer = XmlWriter.Create(xml, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
        {
            writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
            foreach (var page in pages.Where(p => !p.NoIndex))
            {
                writer.WriteStartElement("url");
                writer.WriteElementString("loc", baseUrl + page.Path);
                writer.WriteElementString("lastmod", page.UpdatedDate.ToString("yyyy-MM-dd"));
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        // StringBuilder output is UTF-16 internally; the declaration must match what is sent (UTF-8).
        return xml.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
    }

    private static string Robots(ExtensionContext context, string baseUrl)
    {
        var robots = new StringBuilder("User-agent: *\nDisallow: /admin/\n");
        foreach (var path in context.GetStringList("disallow"))
        {
            robots.Append("Disallow: ").Append(path.ReplaceLineEndings(string.Empty)).Append('\n');
        }

        if (context.GetBool("generateSitemap", true))
        {
            robots.Append("Sitemap: ").Append(baseUrl).Append("/sitemap.xml\n");
        }

        return robots.ToString();
    }

    private static string BaseUrl(AppEnvironment env) => env.AppUrl.TrimEnd('/');
}
