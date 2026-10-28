using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DotNetForge.Web.Services;

/// <summary>
/// Sizes an upload request to the Media Library settings (.docs/features/media-storage.md#upload-limits) instead of a
/// fixed number: the largest per-file limit times <see cref="Files"/>, plus room for the multipart envelope - for the
/// server's request body limit and for the multipart form reader. Runs before antiforgery (which reads the form), like
/// the built-in <c>[RequestSizeLimit]</c>/<c>[RequestFormLimits]</c> it replaces. The per-file limits themselves are
/// checked by <see cref="MediaService.UploadAsync"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class MediaUploadLimitAttribute : Attribute, IAsyncAuthorizationFilter, IOrderedFilter
{
    private const long Envelope = 1024 * 1024;

    /// <summary>How many files one request may carry.</summary>
    public int Files { get; init; } = 1;

    /// <summary>Before <c>ValidateAntiForgeryToken</c> (order 1000), like the built-in size filters.</summary>
    public int Order => 900;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var http = context.HttpContext;
        var settings = await http.RequestServices.GetRequiredService<MediaService>().SettingsAsync(http.RequestAborted);
        var limit = settings.LargestBytes * Files + Envelope;

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } body)
        {
            body.MaxRequestBodySize = limit;
        }

        if (http.Features.Get<IFormFeature>()?.Form is null)
        {
            http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions { MultipartBodyLengthLimit = limit }));
        }
    }
}
