using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using DotNetForge.Abstractions.Storage;
using DotNetForge.Sdk;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TestKit;

/// <summary>
/// Exercises every host slot that takes code. State lives in this extension's own load context; tests observe it
/// only over HTTP (<c>/test-kit/...</c>), like any client would.
/// </summary>
public sealed class TestKitExtension : IExtension
{
    public static readonly ConcurrentQueue<string> AuditActions = new();
    public static readonly MemoryStorage Storage = new();

    public void ConfigureServices(IServiceCollection services, ExtensionContext context)
    {
        services.AddMediaStorage(context, _ => Storage);
        services.AddAuditEventHandler<RecordingHandler>(context);
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, FakeRemoteHandler>("testkit", _ => { });
        services.AddLoginProvider(context, "testkit", "Test Kit");
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ExtensionContext context)
    {
        endpoints.MapGet("/test-kit/ping", () => context.GetString("greeting"));
        endpoints.MapGet("/test-kit/events", () => string.Join('\n', AuditActions));
        endpoints.MapGet("/test-kit/storage", () => string.Join('\n', Storage.Keys));
    }
}

/// <summary>An MVC controller shipped by an extension.</summary>
public sealed class TestKitController : Controller
{
    [HttpGet("/test-kit/controller")]
    public IActionResult Get() => Content("controller-ok");
}

public sealed class RecordingHandler : IAuditEventHandler
{
    public Task HandleAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        TestKitExtension.AuditActions.Enqueue(auditEvent.Action);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for an OAuth provider: the challenge "authenticates" the email posted with the sign-in button (field
/// <c>email</c>) into the host's external cookie and redirects to the host callback, as a real remote handler would.
/// </summary>
public sealed class FakeRemoteHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public FakeRemoteHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var email = Request.HasFormContentType ? Request.Form["email"].ToString() : string.Empty;
        var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Email, email) }, Scheme.Name);
        await Context.SignInAsync(ExternalLogin.SignInScheme, new ClaimsPrincipal(identity), properties);
        Response.Redirect(properties.RedirectUri ?? "/");
    }
}

public sealed class MemoryStorage : IFileStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _objects = new();

    public IEnumerable<string> Keys => _objects.Keys.Order();

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        StorageKey.EnsureValid(key);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        _objects[key] = copy.ToArray();
    }

    public Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(_objects.TryGetValue(key, out var bytes) ? new StoredFile(new MemoryStream(bytes), bytes.Length) : null);

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        _objects.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    public Task<Uri?> GetDownloadUrlAsync(string key, string contentDisposition, TimeSpan lifetime,
        CancellationToken cancellationToken = default) => Task.FromResult<Uri?>(null);
}
