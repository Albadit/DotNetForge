using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using DotNetForge.Web.Services.Webhooks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Webhooks (.docs/features/webhooks.md): the screen's validation and one-time secret, a signed test delivery, events
/// from real media changes, retries with backoff, disabling, the loop guard and permissions - against a fake receiver
/// behind the webhook HTTP client.
/// </summary>
public sealed partial class WebhookTests
{
    private const string Screen = "/admin/webhooks";

    [Fact]
    public async Task Webhooks_are_validated_and_their_secret_is_shown_once()
    {
        var (baseFactory, factory, admin, _) = await StartAsync();
        using var _b = baseFactory;
        using var _f = factory;

        foreach (var (fields, error) in new[]
        {
            (Form("Search", "http://example.com/hook", WebhookEvents.EntryCreate), "must start with https://"),
            (Form("Search", "not a url", WebhookEvents.EntryCreate), "full address"),
            (Form("Search", "https://example.com/hook"), "Choose at least one event"),
            (Form("Search", "https://example.com/hook", WebhookEvents.EntryCreate).With("Headers[0].Key", "").With("Headers[0].Value", "x"), "Every header needs a name"),
            (Form("Search", "https://example.com/hook", WebhookEvents.EntryCreate).With("Headers[0].Key", "Content-Type").With("Headers[0].Value", "x"), "set by the CMS"),
            (Form("Search", "https://example.com/hook", WebhookEvents.EntryCreate).With("Headers[0].Key", "Content-Encoding").With("Headers[0].Value", "gzip"), "set by the CMS"),
            (Form("Search", "https://example.com/hook", WebhookEvents.EntryCreate)
                .With("Headers[0].Key", "X-A").With("Headers[0].Value", new string('a', 1000))
                .With("Headers[1].Key", "X-B").With("Headers[1].Value", new string('b', 1000))
                .With("Headers[2].Key", "X-C").With("Headers[2].Value", new string('c', 1000))
                .With("Headers[3].Key", "X-D").With("Headers[3].Value", new string('d', 1000)), "too long together"),
        })
        {
            var refused = await DotNetForgeWebFactory.PostFormAsync(admin, Screen + "/create", Screen + "/create", fields);
            Assert.Contains(error, WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        }

        var created = await DotNetForgeWebFactory.PostFormAsync(admin, Screen + "/create", Screen + "/create",
            Form("Search", "https://hooks.example.test/cms", WebhookEvents.MediaCreate).With("Headers[0].Key", "Authorization").With("Headers[0].Value", "Bearer abc"));
        var secretUrl = created.Headers.Location!.OriginalString;
        Assert.Matches("^/admin/webhooks/[0-9a-f-]{36}/secret$", secretUrl);
        var secret = SecretRegex().Match(await admin.GetStringAsync(secretUrl)).Groups[1].Value;
        Assert.Equal(64, secret.Length);
        Assert.Equal(HttpStatusCode.Redirect, (await admin.GetAsync(secretUrl)).StatusCode); // shown once

        var duplicate = await DotNetForgeWebFactory.PostFormAsync(admin, Screen + "/create", Screen + "/create",
            Form("search", "https://hooks.example.test/other", WebhookEvents.MediaCreate));
        Assert.Contains("already called", WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync()));

        await baseFactory.WithDbAsync(async db =>
        {
            var webhook = await db.Webhooks.SingleAsync();
            Assert.NotEqual(secret, webhook.Secret); // stored protected
            Assert.Contains("Authorization", webhook.HeadersJson);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.WebhookCreated && a.EntityDisplaySnapshot == "Search"));
        });
    }

    [Fact]
    public async Task A_test_delivery_is_signed_with_the_secret_and_carries_the_custom_headers()
    {
        var (baseFactory, factory, admin, receiver) = await StartAsync();
        using var _b = baseFactory;
        using var _f = factory;
        var (id, secret) = await CreateAsync(admin, Form("Search", "https://hooks.example.test/cms", WebhookEvents.MediaCreate)
            .With("Headers[0].Key", "Authorization").With("Headers[0].Value", "Bearer abc"));

        var tested = await DotNetForgeWebFactory.PostFormAsync(admin, Screen, $"{Screen}/{id}/test", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.Redirect, tested.StatusCode);
        Assert.Contains("Test delivered: 200", WebUtility.HtmlDecode(await admin.GetStringAsync(Screen)));

        var request = Assert.Single(receiver.Requests);
        Assert.Equal("https://hooks.example.test/cms", request.Url);
        Assert.Equal("Bearer abc", request.Headers["Authorization"]);
        Assert.Equal(WebhookPayload.TestEvent, request.Headers[WebhookSender.EventHeader]);
        Assert.Equal(Sign(secret, request.Body), request.Headers[WebhookEvents.SignatureHeader]);
        Assert.StartsWith("DotNetForge-Webhooks", request.Headers["User-Agent"]);

        await baseFactory.WithDbAsync(async db =>
        {
            var delivery = await db.WebhookDeliveries.SingleAsync();
            Assert.Equal(WebhookOutcome.Success, delivery.Outcome);
            Assert.Equal(200, delivery.ResponseStatusCode);
            Assert.NotNull((await db.Webhooks.SingleAsync()).LastDelivery);
        });
        Assert.Contains(WebhookPayload.TestEvent, await admin.GetStringAsync($"{Screen}/{id}/deliveries"));
    }

    [Fact]
    public async Task Media_changes_fire_subscribed_webhooks_and_failures_are_retried_with_backoff()
    {
        var (baseFactory, factory, admin, receiver) = await StartAsync();
        using var _b = baseFactory;
        using var _f = factory;
        var (createId, secret) = await CreateAsync(admin, Form("Uploads", "https://hooks.example.test/uploads", WebhookEvents.MediaCreate));
        await CreateAsync(admin, Form("Deletes", "https://hooks.example.test/deletes", WebhookEvents.MediaDelete));

        receiver.Status = HttpStatusCode.InternalServerError;
        await MediaTests.UploadAsync(admin, "photo.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 1, 2 }, isPublic: true);
        var first = await WaitForAsync(baseFactory, db => db.WebhookDeliveries.AnyAsync(d => d.AttemptNumber == 1 && d.Outcome == WebhookOutcome.Failure));
        Assert.True(first);

        var request = receiver.Requests.Single();
        Assert.Equal("https://hooks.example.test/uploads", request.Url); // only the media.create subscriber
        Assert.Equal(Sign(secret, request.Body), request.Headers[WebhookEvents.SignatureHeader]);
        using (var payload = JsonDocument.Parse(request.Body))
        {
            Assert.Equal(WebhookEvents.MediaCreate, payload.RootElement.GetProperty("event").GetString());
            Assert.Equal("media", payload.RootElement.GetProperty("entity").GetProperty("type").GetString());
            Assert.Equal("photo.png", payload.RootElement.GetProperty("data").GetProperty("fileName").GetString());
        }

        // The failure queued attempt 2 for 30 s later, with the same event and body.
        await baseFactory.WithDbAsync(async db =>
        {
            var retry = await db.WebhookDeliveries.SingleAsync(d => d.AttemptNumber == 2);
            Assert.Equal(WebhookOutcome.Pending, retry.Outcome);
            Assert.InRange((retry.NextAttemptDate!.Value - DateTime.UtcNow).TotalSeconds, 15, 31);
            Assert.Equal(request.Body, retry.RequestBody);
            retry.NextAttemptDate = DateTime.UtcNow.AddSeconds(-1); // due now
            await db.SaveChangesAsync();
        });
        receiver.Status = HttpStatusCode.OK;
        await factory.Services.GetRequiredService<WebhookDispatcher>().RunDueAsync(CancellationToken.None);
        await baseFactory.WithDbAsync(async db =>
        {
            var retry = await db.WebhookDeliveries.SingleAsync(d => d.AttemptNumber == 2);
            Assert.Equal(WebhookOutcome.Success, retry.Outcome);
            Assert.Equal(retry.EventId, (await db.WebhookDeliveries.SingleAsync(d => d.AttemptNumber == 1)).EventId);
        });
        Assert.Equal(receiver.Requests[0].Headers[WebhookSender.DeliveryHeader], receiver.Requests[1].Headers[WebhookSender.DeliveryHeader]);

        // Disabling cancels what is still pending.
        receiver.Status = HttpStatusCode.BadGateway;
        await MediaTests.UploadAsync(admin, "second.png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 3, 4 }, isPublic: true);
        Assert.True(await WaitForAsync(baseFactory, db => db.WebhookDeliveries.AnyAsync(d => d.Outcome == WebhookOutcome.Pending)));
        await DotNetForgeWebFactory.PostFormAsync(admin, Screen, $"{Screen}/{createId}/disable", new Dictionary<string, string>());
        await baseFactory.WithDbAsync(async db =>
        {
            Assert.False(await db.WebhookDeliveries.AnyAsync(d => d.Outcome == WebhookOutcome.Pending));
            Assert.True(await db.WebhookDeliveries.AnyAsync(d => d.Detail == "Cancelled: the webhook was disabled."));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.WebhookDisabled));
        });
    }

    [Fact]
    public async Task Publishing_an_entry_fires_create_and_publish_with_the_entry_data()
    {
        var (baseFactory, factory, admin, receiver) = await StartAsync();
        using var _b = baseFactory;
        using var _f = factory;
        await CreateAsync(admin, Form("Entries", "https://hooks.example.test/entries", WebhookEvents.EntryCreate, WebhookEvents.EntryPublish));

        await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/content-types/new", "/admin/content-types/create",
            new Dictionary<string, string> { ["template"] = "blog-posts", ["name"] = string.Empty });
        var saved = await DotNetForgeWebFactory.PostFormAsync(admin, "/admin/content/categories/new", "/admin/content/categories/new",
            new Dictionary<string, string> { ["f.name"] = "Design", ["intent"] = "publish" });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);

        Assert.True(await WaitForAsync(baseFactory, db => db.WebhookDeliveries.CountAsync(d => d.Outcome == WebhookOutcome.Success).ContinueWith(t => t.Result == 2)));
        var events = receiver.Requests.Select(r => JsonDocument.Parse(r.Body).RootElement).ToList();
        Assert.Equal(new[] { WebhookEvents.EntryCreate, WebhookEvents.EntryPublish }, events.Select(e => e.GetProperty("event").GetString()).Order());
        var published = events.Single(e => e.GetProperty("event").GetString() == WebhookEvents.EntryPublish);
        Assert.Equal("entry", published.GetProperty("entity").GetProperty("type").GetString());
        Assert.Equal("categories", published.GetProperty("data").GetProperty("contentType").GetString());
        Assert.True(published.GetProperty("data").GetProperty("published").GetBoolean());
        Assert.Equal("Design", published.GetProperty("data").GetProperty("publishedData").GetProperty("name").GetString());
    }

    [Fact]
    public async Task Changes_made_by_a_webhook_delivery_fire_nothing_and_editors_cant_open_the_screen()
    {
        var (baseFactory, factory, admin, receiver) = await StartAsync();
        using var _b = baseFactory;
        using var _f = factory;
        await CreateAsync(admin, Form("Uploads", "https://hooks.example.test/uploads", WebhookEvents.MediaCreate));

        // A receiver calling back with the delivery header (loop guard).
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(admin, "/admin/media");
        using var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        var file = new ByteArrayContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 9, 9 });
        file.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "files", "loop.png");
        using var upload = new HttpRequestMessage(HttpMethod.Post, "/admin/media/upload") { Content = form };
        upload.Headers.Add(WebhookSender.DeliveryHeader, Guid.NewGuid().ToString());
        Assert.Equal(HttpStatusCode.Redirect, (await admin.SendAsync(upload)).StatusCode);
        await Task.Delay(500);
        await factory.Services.GetRequiredService<WebhookDispatcher>().RunDueAsync(CancellationToken.None);
        await baseFactory.WithDbAsync(async db => Assert.False(await db.WebhookDeliveries.AnyAsync()));
        Assert.Empty(receiver.Requests);

        await baseFactory.CreateUserAsync("editor@example.com", "Edit0rPass1", Roles.Editor);
        var editor = await SignInAsync(factory, "editor@example.com", "Edit0rPass1");
        Assert.Contains("/account/denied", (await editor.GetAsync(Screen)).Headers.Location?.OriginalString);
        Assert.DoesNotContain("href=\"/admin/webhooks\"", await editor.GetStringAsync("/admin"));
    }

    // ---- Helpers -----------------------------------------------------------------------------------------------------

    private static async Task<(DotNetForgeWebFactory Base, WebApplicationFactory<Program> Factory, HttpClient Admin, FakeReceiver Receiver)> StartAsync()
    {
        var baseFactory = new DotNetForgeWebFactory();
        await baseFactory.InstallAsync();
        var receiver = new FakeReceiver();
        var factory = baseFactory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddHttpClient(WebhookSender.ClientName).ConfigurePrimaryHttpMessageHandler(() => receiver)));
        return (baseFactory, factory, await SignInAsync(factory, DotNetForgeWebFactory.AdminEmail, DotNetForgeWebFactory.AdminPassword), receiver);
    }

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> factory, string email, string password)
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        var signIn = await DotNetForgeWebFactory.PostFormAsync(client, "/login", "/login", new Dictionary<string, string> { ["email"] = email, ["password"] = password });
        Assert.Equal(HttpStatusCode.Redirect, signIn.StatusCode);
        return client;
    }

    private static async Task<(Guid Id, string Secret)> CreateAsync(HttpClient admin, Dictionary<string, string> fields)
    {
        var created = await DotNetForgeWebFactory.PostFormAsync(admin, Screen + "/create", Screen + "/create", fields);
        var location = created.Headers.Location?.OriginalString ?? throw new InvalidOperationException(await created.Content.ReadAsStringAsync());
        var secret = SecretRegex().Match(await admin.GetStringAsync(location)).Groups[1].Value;
        return (Guid.Parse(location.Split('/')[3]), secret);
    }

    private static Dictionary<string, string> Form(string name, string url, params string[] events)
    {
        var form = new Dictionary<string, string> { ["Name"] = name, ["Url"] = url, ["Enabled"] = "true" };
        for (var i = 0; i < events.Length; i++)
        {
            form[$"Events[{i}]"] = events[i];
        }

        return form;
    }

    private static async Task<bool> WaitForAsync(DotNetForgeWebFactory factory, Func<DotNetForge.Data.DotNetForgeDbContext, Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            var met = false;
            await factory.WithDbAsync(async db => met = await condition(db));
            if (met)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    private static string Sign(string secret, string body) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body)));

    [GeneratedRegex("<span id=\"secret-text\" hidden>([0-9a-f]+)</span>")]
    private static partial Regex SecretRegex();

    private sealed record ReceivedRequest(string Url, string Body, Dictionary<string, string> Headers);

    /// <summary>The receiving end: records each request and answers <see cref="Status"/>.</summary>
    private sealed class FakeReceiver : HttpMessageHandler
    {
        private readonly object _gate = new();

        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;

        public List<ReceivedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(" ", h.Value), StringComparer.OrdinalIgnoreCase);
            lock (_gate)
            {
                Requests.Add(new ReceivedRequest(request.RequestUri!.AbsoluteUri, body, headers));
            }

            return new HttpResponseMessage(Status) { Content = new StringContent("{\"received\":true}") };
        }
    }
}

internal static class FormExtensions
{
    public static Dictionary<string, string> With(this Dictionary<string, string> form, string key, string value)
    {
        form[key] = value;
        return form;
    }
}
