using System.Diagnostics;
using System.Net;
using DotNetForge.Shared.Constants;
using DotNetForge.Web.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Server-side sessions (.docs/features/authentication.md#sessions): the cookie only says who and which session, so what
/// the database says counts from the next request, and a session that ended is over wherever its cookie went.
/// </summary>
public sealed class SessionTests
{
    private const string Password = "Edit0rPass1";
    private const string Email = "person@example.com";

    private static async Task ChangeRolesAsync(DotNetForgeWebFactory factory, Guid userId, params string[] roles)
    {
        using var scope = factory.Services.CreateScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<DotNetForge.Data.DotNetForgeDbContext>();
        var roleIds = await db.Roles.Where(r => roles.Contains(r.Name)).Select(r => r.Id).ToListAsync();
        var actor = await db.Users.Where(u => u.Email == DotNetForgeWebFactory.AdminEmail).Select(u => u.Id).SingleAsync();
        var error = await services.GetRequiredService<UserManagementService>()
            .UpdateAsync(userId, new UserInput { Email = Email, RoleIds = roleIds }, new ManagementActor(actor, IsSuperAdmin: true));
        Assert.Null(error);
    }

    private static Task<T> WithUsersAsync<T>(DotNetForgeWebFactory factory, Func<UserManagementService, Guid, Task<T>> action)
    {
        var scope = factory.Services.CreateScope();
        var actor = scope.ServiceProvider.GetRequiredService<DotNetForge.Data.DotNetForgeDbContext>()
            .Users.Where(u => u.Email == DotNetForgeWebFactory.AdminEmail).Select(u => u.Id).Single();
        return action(scope.ServiceProvider.GetRequiredService<UserManagementService>(), actor);
    }

    private static void AssertSignedOut(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/login", response.Headers.Location?.OriginalString);
    }

    /// <summary>A client that keeps no cookies, for sending one exactly as a copy of it would be.</summary>
    private static HttpClient Bare(DotNetForgeWebFactory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        AllowAutoRedirect = false,
        HandleCookies = false,
        BaseAddress = new Uri("https://localhost"),
    });

    private static async Task<(HttpClient Client, string Cookie)> SignInWithCookieAsync(DotNetForgeWebFactory factory)
    {
        var client = factory.CreateNoRedirectClient();
        var response = await DotNetForgeWebFactory.PostFormAsync(client, "/login", "/login",
            new Dictionary<string, string> { ["email"] = Email, ["password"] = Password });
        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("dnf.auth=", StringComparison.Ordinal));
        return (client, cookie.Split(';')[0]);
    }

    private static Task<HttpResponseMessage> GetWithCookieAsync(HttpClient client, string cookie, string path)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Cookie", cookie);
        return client.SendAsync(request);
    }

    [Fact]
    public async Task A_role_given_or_taken_counts_from_the_next_request_without_signing_in_again()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var id = await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var person = await factory.SignInAsync(Email, Password);
        Assert.NotEqual(HttpStatusCode.OK, (await person.GetAsync("/admin/users")).StatusCode);

        await ChangeRolesAsync(factory, id, Roles.SuperAdmin);
        Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/admin/users")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/admin/plugins")).StatusCode);

        // The hole this closes: the cookie still says Super Admin, and sliding renewal would keep it alive for ever.
        await ChangeRolesAsync(factory, id, Roles.Editor);
        Assert.NotEqual(HttpStatusCode.OK, (await person.GetAsync("/admin/users")).StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, (await person.GetAsync("/admin/plugins")).StatusCode);
    }

    [Fact]
    public async Task Signing_out_ends_the_session_on_the_server_and_only_that_one()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var (laptop, cookie) = await SignInWithCookieAsync(factory);
        var phone = await factory.SignInAsync(Email, Password);
        using var thief = Bare(factory);
        Assert.Equal(HttpStatusCode.OK, (await GetWithCookieAsync(thief, cookie, "/admin/pages")).StatusCode);

        await DotNetForgeWebFactory.PostFormAsync(laptop, "/admin/pages", "/account/logout", new Dictionary<string, string>());

        AssertSignedOut(await laptop.GetAsync("/admin/pages"));
        AssertSignedOut(await GetWithCookieAsync(thief, cookie, "/admin/pages")); // a copy made before signing out
        Assert.Equal(HttpStatusCode.OK, (await phone.GetAsync("/admin/pages")).StatusCode);
    }

    [Fact]
    public async Task An_expired_session_is_over_however_recently_the_cookie_was_renewed()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var id = await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var person = await factory.SignInAsync(Email, Password);

        await factory.WithDbAsync(async db =>
        {
            await db.UserSessions.Where(s => s.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresDate, DateTime.UtcNow.AddMinutes(-1)));
        });
        using (var scope = factory.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<UserSessionService>().Forget(id);
        }

        AssertSignedOut(await person.GetAsync("/admin/pages"));
    }

    [Fact]
    public async Task A_new_sign_in_clears_out_sessions_that_have_run_out()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var id = await factory.CreateUserAsync(Email, Password, Roles.Editor);
        await factory.SignInAsync(Email, Password);
        await factory.WithDbAsync(db => db.UserSessions.Where(s => s.UserId == id).ExecuteUpdateAsync(s => s.SetProperty(x => x.ExpiresDate, DateTime.UtcNow.AddMinutes(-1))));

        await factory.SignInAsync(Email, Password);

        await factory.WithDbAsync(async db => Assert.Equal(1, await db.UserSessions.CountAsync(s => s.UserId == id)));
    }

    [Fact]
    public async Task Disabling_or_deleting_a_user_ends_their_sessions_at_once()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var id = await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var person = await factory.SignInAsync(Email, Password);

        await WithUsersAsync(factory, (users, actor) => users.SetStatusAsync(id, enabled: false, new ManagementActor(actor, IsSuperAdmin: true)));
        AssertSignedOut(await person.GetAsync("/admin/pages"));

        await WithUsersAsync(factory, (users, actor) => users.SetStatusAsync(id, enabled: true, new ManagementActor(actor, IsSuperAdmin: true)));
        var again = await factory.SignInAsync(Email, Password);
        await WithUsersAsync(factory, (users, actor) => users.DeleteAsync(id, new ManagementActor(actor, IsSuperAdmin: true)));
        AssertSignedOut(await again.GetAsync("/admin/pages"));
        await factory.WithDbAsync(async db => Assert.False(await db.UserSessions.AnyAsync(s => s.UserId == id)));
    }

    [Fact]
    public async Task Changing_a_password_ends_the_other_sessions_and_keeps_this_one()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var here = await factory.SignInAsync(Email, Password);
        var elsewhere = await factory.SignInAsync(Email, Password);

        await DotNetForgeWebFactory.PostFormAsync(here, "/admin/profile", "/admin/profile/password", new Dictionary<string, string>
        {
            ["CurrentPassword"] = Password,
            ["NewPassword"] = "An0ther-Passw0rd",
            ["ConfirmPassword"] = "An0ther-Passw0rd",
        });

        Assert.Equal(HttpStatusCode.OK, (await here.GetAsync("/admin/pages")).StatusCode);
        AssertSignedOut(await elsewhere.GetAsync("/admin/pages"));
    }

    [Fact]
    public async Task An_administrator_setting_a_password_ends_every_session_of_that_user()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var id = await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var person = await factory.SignInAsync(Email, Password);
        List<Guid> editor = new();
        await factory.WithDbAsync(async db => editor = await db.Roles.Where(r => r.Name == Roles.Editor).Select(r => r.Id).ToListAsync());

        await WithUsersAsync(factory, (users, actor) => users.UpdateAsync(id,
            new UserInput { Email = Email, Password = "An0ther-Passw0rd", RoleIds = editor }, new ManagementActor(actor, IsSuperAdmin: true)));

        AssertSignedOut(await person.GetAsync("/admin/pages"));
    }

    [Fact]
    public async Task Saving_your_profile_keeps_your_session()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var id = await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var person = await factory.SignInAsync(Email, Password);

        await DotNetForgeWebFactory.PostFormAsync(person, "/admin/profile", "/admin/profile", new Dictionary<string, string> { ["FirstName"] = "New", ["Email"] = Email });

        Assert.Equal(HttpStatusCode.OK, (await person.GetAsync("/admin/pages")).StatusCode);
        await factory.WithDbAsync(async db => Assert.Equal(1, await db.UserSessions.CountAsync(s => s.UserId == id)));
    }

    [Fact]
    public async Task A_cookie_that_was_changed_counts_for_nothing()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var (_, cookie) = await SignInWithCookieAsync(factory);
        using var client = Bare(factory);

        AssertSignedOut(await GetWithCookieAsync(client, cookie[..^6] + "AAAAAA", "/admin/pages"));
    }

    [Fact]
    public async Task The_session_cookie_is_out_of_reach_of_scripts_and_other_sites()
    {
        using var factory = new DotNetForgeWebFactory("Production");
        await factory.InstallAsync();
        await factory.CreateUserAsync(Email, Password, Roles.Editor);
        var client = factory.CreateNoRedirectClient();
        var response = await DotNetForgeWebFactory.PostFormAsync(client, "/login", "/login",
            new Dictionary<string, string> { ["email"] = Email, ["password"] = Password });

        var cookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("dnf.auth=", StringComparison.Ordinal)).ToLowerInvariant();
        Assert.Contains("httponly", cookie);
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=lax", cookie);
    }

    [Fact]
    public async Task Repeated_screens_ask_the_database_nothing_about_roles_or_permissions()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/settings")).StatusCode);

        var commands = new List<string>();
        using var subscription = DiagnosticListener.AllListeners.Subscribe(new CommandObserver(commands));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/admin/settings")).StatusCode);
        }

        lock (commands)
        {
            Assert.DoesNotContain(commands, c => c.Contains("\"RolePermissions\"", StringComparison.Ordinal) || c.Contains("\"UserRoles\"", StringComparison.Ordinal) || c.Contains("\"UserSessions\"", StringComparison.Ordinal));
        }
    }

    private sealed class CommandObserver(List<string> commands) : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "Microsoft.EntityFrameworkCore")
            {
                value.Subscribe(this);
            }
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Value is Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData data)
            {
                lock (commands)
                {
                    commands.Add(data.Command.CommandText);
                }
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
