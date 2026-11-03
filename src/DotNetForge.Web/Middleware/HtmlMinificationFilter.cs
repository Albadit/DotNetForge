using System.Text;
using DotNetForge.Core.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DotNetForge.Web.Middleware;

/// <summary>
/// Minifies the HTML a view renders (<see cref="HtmlMinifier"/>) - only in a Release build of the app, the one
/// <c>dotnet publish</c> makes; a Debug build (<c>dotnet run</c>) answers with the views' markup as written
/// (.docs/guides/development.md#development-and-production-builds). Views only: files, JSON and redirects stream untouched.
/// </summary>
public sealed class HtmlMinificationFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is not (ViewResult or PartialViewResult or ViewComponentResult))
        {
            await next();
            return;
        }

        var response = context.HttpContext.Response;
        var body = response.Body;
        using var rendered = new MemoryStream();
        response.Body = rendered;
        try
        {
            await next();
        }
        finally
        {
            response.Body = body;
        }

        var bytes = rendered.GetBuffer().AsMemory(0, (int)rendered.Length);
        if ((response.ContentType ?? string.Empty).StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
        {
            bytes = Encoding.UTF8.GetBytes(HtmlMinifier.Minify(Encoding.UTF8.GetString(bytes.Span)));
        }

        response.ContentLength = null;
        await body.WriteAsync(bytes, context.HttpContext.RequestAborted);
    }
}
