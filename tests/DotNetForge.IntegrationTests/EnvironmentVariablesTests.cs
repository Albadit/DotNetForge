using System.Net;
using DotNetForge.Sdk;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>Settings → Environment Variables: secrets, config, per-environment values (.docs/pages/environment-variables.md).</summary>
public sealed class EnvironmentVariablesTests
{
    private const string Screen = "/admin/environment-variables";

    private static Task<HttpResponseMessage> SaveAsync(HttpClient admin, Dictionary<string, string> fields) =>
        DotNetForgeWebFactory.PostFormAsync(admin, Screen + "/new", Screen + "/save", fields);

    private static async Task<string?> ReadAsync(DotNetForgeWebFactory factory, string key)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IEnvironmentVariables>().GetAsync(key);
    }

    private static async Task<Setting> RowAsync(DotNetForgeWebFactory factory, string key, string environment = "")
    {
        Setting row = null!;
        await factory.WithDbAsync(async db =>
            row = await db.Settings.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Key == key && s.Environment == environment));
        return row;
    }

    [Fact]
    public async Task A_config_variable_is_added_shown_and_changed_with_its_key_fixed()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        Assert.Contains($"href=\"{Screen}/new\"", await admin.GetStringAsync(Screen));
        Assert.Equal(HttpStatusCode.Redirect, (await SaveAsync(admin, new()
        {
            ["Key"] = "site.tagline",
            ["Value"] = "Hello",
            ["IsSecret"] = "false",
            ["Environment"] = "",
            ["Note"] = "Shown in the footer",
        })).StatusCode);

        var list = await admin.GetStringAsync(Screen);
        Assert.Contains("data-env-plain hidden>Hello</code>", list); // a config value can be shown with the eye
        Assert.Contains("data-copy-text=\"site.tagline=Hello\"", list); // copied as KEY=VALUE
        Assert.Contains("Shown in the footer", list);
        var row = await RowAsync(factory, "site.tagline");
        Assert.Equal(("Hello", false), (row.Value, row.IsSecret));
        Assert.NotNull(row.UpdatedById);

        var edit = await admin.GetStringAsync($"{Screen}/{row.Id}");
        Assert.Contains("readonly", edit);
        await DotNetForgeWebFactory.PostFormAsync(admin, $"{Screen}/{row.Id}", Screen + "/save", new Dictionary<string, string>
        {
            ["Id"] = row.Id.ToString(),
            ["Key"] = "renamed",
            ["Value"] = "Hi",
            ["IsSecret"] = "false",
            ["Environment"] = "",
        });
        Assert.Equal("Hi", (await RowAsync(factory, "site.tagline")).Value); // the key stays
        Assert.Equal("Hi", await ReadAsync(factory, "site.tagline"));
    }

    [Fact]
    public async Task A_secret_is_encrypted_never_shown_and_kept_when_left_empty()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        await SaveAsync(admin, new() { ["Key"] = "PAYMENT_API_KEY", ["Value"] = "sk_live_123", ["IsSecret"] = "true", ["Environment"] = "" });

        var row = await RowAsync(factory, "PAYMENT_API_KEY");
        Assert.True(row.IsSecret);
        Assert.DoesNotContain("sk_live_123", row.Value); // encrypted at rest
        var list = await admin.GetStringAsync(Screen);
        Assert.DoesNotContain("sk_live_123", list);
        Assert.DoesNotContain("data-copy-text=\"PAYMENT_API_KEY", list); // a secret can't be copied
        Assert.DoesNotContain("sk_live_123", await admin.GetStringAsync($"{Screen}/{row.Id}"));
        Assert.Equal("sk_live_123", await ReadAsync(factory, "PAYMENT_API_KEY")); // extensions read it decrypted

        // An empty value keeps the secret; it can only become config with a new value.
        await DotNetForgeWebFactory.PostFormAsync(admin, $"{Screen}/{row.Id}", Screen + "/save", new Dictionary<string, string>
        {
            ["Id"] = row.Id.ToString(),
            ["Value"] = "",
            ["IsSecret"] = "true",
            ["Environment"] = "",
            ["Note"] = "Rotate in the payment dashboard",
        });
        Assert.Equal("sk_live_123", await ReadAsync(factory, "PAYMENT_API_KEY"));
        var refused = await DotNetForgeWebFactory.PostFormAsync(admin, $"{Screen}/{row.Id}", Screen + "/save", new Dictionary<string, string>
        {
            ["Id"] = row.Id.ToString(),
            ["Value"] = "",
            ["IsSecret"] = "false",
            ["Environment"] = "",
        });
        Assert.Contains("enter the value again", System.Net.WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        Assert.True((await RowAsync(factory, "PAYMENT_API_KEY")).IsSecret);
    }

    [Fact]
    public async Task The_running_environment_reads_its_own_value_over_the_one_for_all()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var here = SettingEnvironment.Current;
        var elsewhere = here == "Production" ? "Development" : "Production";

        await SaveAsync(admin, new() { ["Key"] = "api.url", ["Value"] = "https://all", ["IsSecret"] = "false", ["Environment"] = "" });
        await SaveAsync(admin, new() { ["Key"] = "api.url", ["Value"] = "https://elsewhere", ["IsSecret"] = "false", ["Environment"] = elsewhere });
        Assert.Equal("https://all", await ReadAsync(factory, "api.url"));

        await SaveAsync(admin, new() { ["Key"] = "api.url", ["Value"] = "https://here", ["IsSecret"] = "false", ["Environment"] = here });
        Assert.Equal("https://here", await ReadAsync(factory, "api.url"));

        // One value per key and environment; the app's own context only sees its environment and all environments.
        var duplicate = await SaveAsync(admin, new() { ["Key"] = "api.url", ["Value"] = "x", ["IsSecret"] = "false", ["Environment"] = here });
        Assert.Contains("already has a value", await duplicate.Content.ReadAsStringAsync());
        await factory.WithDbAsync(async db =>
        {
            Assert.Equal(2, await db.Settings.CountAsync(s => s.Key == "api.url"));
            Assert.Equal(3, await db.Settings.IgnoreQueryFilters().CountAsync(s => s.Key == "api.url"));
        });
    }

    [Fact]
    public async Task Keys_of_other_screens_are_refused_and_variables_can_be_deleted()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var managed = await SaveAsync(admin, new() { ["Key"] = "users.signUps", ["Value"] = "true", ["IsSecret"] = "false", ["Environment"] = "" });
        Assert.Contains("is managed on Advanced Settings", System.Net.WebUtility.HtmlDecode(await managed.Content.ReadAsStringAsync()));

        await SaveAsync(admin, new() { ["Key"] = "old.flag", ["Value"] = "1", ["IsSecret"] = "false", ["Environment"] = "" });
        var row = await RowAsync(factory, "old.flag");
        await DotNetForgeWebFactory.PostFormAsync(admin, Screen, $"{Screen}/{row.Id}/delete", new Dictionary<string, string>());
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Settings.IgnoreQueryFilters().AnyAsync(s => s.Key == "old.flag"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.SettingsDeleted && a.EntityId == "old.flag"));
        });
    }

    [Fact]
    public async Task The_old_settings_addresses_lead_here()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var client = await factory.SignInAsync(); // doesn't follow redirects

        var moved = await client.GetAsync("/admin/settings");
        Assert.Equal(HttpStatusCode.MovedPermanently, moved.StatusCode);
        Assert.Equal(Screen, moved.Headers.Location?.OriginalString);
        Assert.Equal(Screen + "/new", (await client.GetAsync("/admin/settings/new")).Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Data_management_exports_the_variables_of_every_environment()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var elsewhere = SettingEnvironment.Current == "Production" ? "Development" : "Production";
        await factory.WithDbAsync(async db =>
        {
            db.Settings.Add(new Setting { Key = "only.elsewhere", Value = "x", Environment = elsewhere });
            await db.SaveChangesAsync();
        });

        string json = string.Empty;
        await factory.WithDbAsync(async db =>
        {
            using var file = new MemoryStream();
            await DotNetForge.Web.Services.TransferFile.WriteAsync(db, file, "postgresql", CancellationToken.None);
            file.Position = 0;
            using var zip = new System.IO.Compression.ZipArchive(file);
            var entry = zip.Entries.Single(e => e.FullName.EndsWith("/Setting.json", StringComparison.Ordinal));
            using var reader = new StreamReader(entry.Open());
            json = await reader.ReadToEndAsync();
        });
        Assert.Contains("only.elsewhere", json); // hidden from the running app, still part of the site
    }
}
