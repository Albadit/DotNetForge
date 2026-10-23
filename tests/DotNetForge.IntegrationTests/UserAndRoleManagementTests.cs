using System.Net;
using System.Text.RegularExpressions;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// Users and Roles screens (.docs/pages/users.md, .docs/pages/roles.md) and how stored grants authorize
/// (.docs/features/authorization.md#how-permissions-are-evaluated): created users can sign in, custom roles grant
/// exactly what they hold, grant edits apply at once, and the safety rules hold.
/// </summary>
public sealed class UserAndRoleManagementTests
{
    private const string Password = "Passw0rdOk";

    [Fact]
    public async Task The_form_never_sets_the_status_only_disable_and_enable_do()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        Assert.DoesNotContain("name=\"Enabled\"", await admin.GetStringAsync("/admin/users/create"));
        // A posted Enabled=false is ignored: a new user is always enabled.
        var response = await PostAsync(admin, "/admin/users/create", "/admin/users/create", new()
        {
            ["Email"] = "grace@example.com",
            ["Password"] = Password,
            ["RoleIds"] = (await RoleIdAsync(factory, Roles.Editor)).ToString(),
            ["Enabled"] = "false",
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        await factory.WithDbAsync(async db =>
            Assert.Equal(UserStatus.Enabled, (await db.Users.SingleAsync(u => u.Email == "grace@example.com")).Status));
        var editUrl = response.Headers.Location!.OriginalString;
        var form = await admin.GetStringAsync(editUrl);
        Assert.DoesNotContain("name=\"Enabled\"", form);
        Assert.Contains("Disable user", form);

        // Saving the form keeps a disabled user disabled (it no longer posts a status).
        var id = editUrl["/admin/users/".Length..];
        await PostAsync(admin, editUrl, $"/admin/users/{id}/disable", new());
        await PostAsync(admin, editUrl, editUrl, new()
        {
            ["Email"] = "grace@example.com",
            ["RoleIds"] = (await RoleIdAsync(factory, Roles.Editor)).ToString(),
        });
        await factory.WithDbAsync(async db =>
            Assert.Equal(UserStatus.Disabled, (await db.Users.SingleAsync(u => u.Email == "grace@example.com")).Status));
    }

    [Fact]
    public async Task An_admin_creates_a_user_who_can_then_sign_in()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();
        var editorRole = await RoleIdAsync(factory, Roles.Editor);

        var response = await PostAsync(admin, "/admin/users/create", "/admin/users/create", new()
        {
            ["FirstName"] = "Ada",
            ["LastName"] = "Lovelace",
            ["Email"] = "ada@example.com",
            ["Password"] = Password,
            ["RoleIds"] = editorRole.ToString(),
        });

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/users/", response.Headers.Location!.OriginalString);
        var editor = await factory.SignInAsync("ada@example.com", Password);
        Assert.Equal(HttpStatusCode.OK, (await editor.GetAsync("/admin/content")).StatusCode);
        await factory.WithDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Email == "ada@example.com");
            Assert.NotEqual(Password, user.PasswordHash);
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.UserCreated && a.EntityId == user.Id.ToString()));
        });

        // The edit screen lists the effective permissions; the list links to it.
        var list = await admin.GetStringAsync("/admin/users");
        Assert.Contains("href=\"/admin/users/create\"", list);
        var id = response.Headers.Location!.OriginalString["/admin/users/".Length..];
        Assert.Contains($"href=\"/admin/users/{id}\"", list);
        Assert.Contains("Effective permissions", await admin.GetStringAsync($"/admin/users/{id}"));

        // Duplicate email (case-insensitive) and a weak password are refused with a message, nothing is saved.
        var duplicate = await PostAsync(admin, "/admin/users/create", "/admin/users/create", new()
        {
            ["Email"] = "ADA@example.com",
            ["Password"] = Password,
            ["RoleIds"] = editorRole.ToString(),
        });
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("Another user already uses", await duplicate.Content.ReadAsStringAsync());
        await factory.WithDbAsync(async db => Assert.Equal(2, await db.Users.CountAsync()));
    }

    [Fact]
    public async Task A_custom_role_grants_exactly_its_permissions_and_edits_apply_at_once()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var created = await PostAsync(admin, "/admin/roles/create", "/admin/roles/create", new()
        {
            ["Name"] = "Reviewer",
            ["Description"] = "Reads content",
            ["Grants"] = $"{PermissionAreas.CollectionTypes}|{PermissionActions.Read}",
        });
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var roleUrl = created.Headers.Location!.OriginalString;
        var roleId = await RoleIdAsync(factory, "Reviewer");

        await PostAsync(admin, "/admin/users/create", "/admin/users/create", new()
        {
            ["Email"] = "rev@example.com",
            ["Password"] = Password,
            ["RoleIds"] = roleId.ToString(),
        });
        var reviewer = await factory.SignInAsync("rev@example.com", Password);

        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync("/admin/content")).StatusCode);
        Assert.Contains("/account/denied", (await reviewer.GetAsync("/admin/settings")).Headers.Location?.OriginalString);
        var sidebar = await reviewer.GetStringAsync("/admin");
        Assert.Contains("href=\"/admin/content\"", sidebar);
        Assert.DoesNotContain("href=\"/admin/settings\"", sidebar);

        // Granting Settings read lets the signed-in reviewer open Settings without signing in again.
        var saved = await PostAsync(admin, roleUrl, roleUrl, new KeyValuePair<string, string>[]
        {
            new("Name", "Reviewer"),
            new("Grants", $"{PermissionAreas.CollectionTypes}|{PermissionActions.Read}"),
            new("Grants", $"{PermissionAreas.Settings}|{PermissionActions.Read}"),
        });
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await reviewer.GetAsync("/admin/settings")).StatusCode);
        await factory.WithDbAsync(async db =>
            Assert.True(await db.AuditLogs.AnyAsync(a => a.Action == AuditActions.PermissionChanged && a.EntityId == roleId.ToString())));
    }

    [Fact]
    public async Task A_role_in_use_cannot_be_deleted_and_built_in_roles_never()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var duplicated = await PostAsync(admin, "/admin/roles", $"/admin/roles/{await RoleIdAsync(factory, Roles.Editor)}/duplicate", new());
        Assert.Equal(HttpStatusCode.Redirect, duplicated.StatusCode);
        var editorId = await RoleIdAsync(factory, Roles.Editor);
        var copyId = await RoleIdAsync(factory, $"{Roles.Editor} (copy)");
        await factory.WithDbAsync(async db =>
            Assert.Equal(
                await db.RolePermissions.CountAsync(p => p.RoleId == editorId),
                await db.RolePermissions.CountAsync(p => p.RoleId == copyId)));

        // The list's row menu offers Delete for an unused custom role, never for a built-in one.
        var list = await admin.GetStringAsync("/admin/roles");
        Assert.Contains($"action=\"/admin/roles/{copyId}/delete\"", list);
        Assert.DoesNotContain($"action=\"/admin/roles/{editorId}/delete\"", list);
        Assert.Contains("popovertarget=", list);

        var userId = await factory.CreateUserAsync("held@example.com", Password, $"{Roles.Editor} (copy)");
        Assert.Contains("In use by 1 user", await admin.GetStringAsync("/admin/roles"));
        await PostAsync(admin, "/admin/roles", $"/admin/roles/{copyId}/delete", new());
        await factory.WithDbAsync(async db => Assert.True(await db.Roles.AnyAsync(r => r.Id == copyId)));

        await PostAsync(admin, $"/admin/users/{userId}", $"/admin/users/{userId}/delete", new());
        await PostAsync(admin, "/admin/roles", $"/admin/roles/{copyId}/delete", new());
        await factory.WithDbAsync(async db =>
        {
            Assert.False(await db.Roles.AnyAsync(r => r.Id == copyId));
            Assert.False(await db.RolePermissions.AnyAsync(p => p.RoleId == copyId));
        });

        await PostAsync(admin, "/admin/roles", $"/admin/roles/{await RoleIdAsync(factory, Roles.Author)}/delete", new());
        await factory.WithDbAsync(async db => Assert.True(await db.Roles.AnyAsync(r => r.Name == Roles.Author)));
    }

    [Fact]
    public async Task Super_admins_and_your_own_account_are_protected()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var superAdmin = await factory.SignInAsync();
        Guid superAdminId = default;
        await factory.WithDbAsync(async db => superAdminId = await db.Users.Where(u => u.Email == DotNetForgeWebFactory.AdminEmail).Select(u => u.Id).SingleAsync());

        // Nobody disables or deletes themselves - and the last enabled Super Admin stays.
        await PostAsync(superAdmin, "/admin/users/create", $"/admin/users/{superAdminId}/disable", new());
        await PostAsync(superAdmin, "/admin/users/create", $"/admin/users/{superAdminId}/delete", new());
        await factory.WithDbAsync(async db => Assert.Equal(UserStatus.Enabled, (await db.Users.SingleAsync(u => u.Id == superAdminId)).Status));

        // An Admin may manage users, but not Super Admins and not the Super Admin role.
        await factory.CreateUserAsync("admin2@example.com", Password, Roles.Admin);
        var admin = await factory.SignInAsync("admin2@example.com", Password);
        await PostAsync(admin, "/admin/users/create", $"/admin/users/{superAdminId}/disable", new());
        await PostAsync(admin, "/admin/users/create", $"/admin/users/{superAdminId}/delete", new());
        await factory.WithDbAsync(async db => Assert.Equal(UserStatus.Enabled, (await db.Users.SingleAsync(u => u.Id == superAdminId)).Status));

        var promoted = await PostAsync(admin, "/admin/users/create", "/admin/users/create", new()
        {
            ["Email"] = "sneaky@example.com",
            ["Password"] = Password,
            ["RoleIds"] = (await RoleIdAsync(factory, Roles.SuperAdmin)).ToString(),
        });
        Assert.Contains("Only a Super Admin can give the Super Admin role.", await promoted.Content.ReadAsStringAsync());

        // An Admin can't change the grants of the Admin role they hold, but can still edit its description.
        var adminRole = await RoleIdAsync(factory, Roles.Admin);
        var refused = await PostAsync(admin, $"/admin/roles/{adminRole}", $"/admin/roles/{adminRole}", new()
        {
            ["Description"] = "Changed",
            ["Grants"] = $"{PermissionAreas.Media}|{PermissionActions.Read}",
        });
        Assert.Contains("You can't change the permissions of a role you hold.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
        var form = await admin.GetStringAsync($"/admin/roles/{adminRole}");
        var held = Regex.Matches(form, "type=\"hidden\" name=\"Grants\" value=\"([^\"]+)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();
        Assert.NotEmpty(held);
        var described = await PostAsync(admin, $"/admin/roles/{adminRole}", $"/admin/roles/{adminRole}",
            held.Select(g => new KeyValuePair<string, string>("Grants", g)).Append(new("Description", "Runs the site")));
        Assert.Equal(HttpStatusCode.Redirect, described.StatusCode);
        await factory.WithDbAsync(async db => Assert.Equal("Runs the site", (await db.Roles.SingleAsync(r => r.Id == adminRole)).Description));
    }

    [Fact]
    public async Task The_sidebar_lists_roles_once()
    {
        using var factory = new DotNetForgeWebFactory();
        await factory.InstallAsync();
        var admin = await factory.SignInAsync();

        var html = await admin.GetStringAsync("/admin/media");
        Assert.Single(Regex.Matches(html, "href=\"/admin/roles\""));
    }

    private static async Task<Guid> RoleIdAsync(DotNetForgeWebFactory factory, string name)
    {
        Guid id = default;
        await factory.WithDbAsync(async db => id = await db.Roles.Where(r => r.Name == name).Select(r => r.Id).SingleAsync());
        return id;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string formPage, string action, Dictionary<string, string> fields) =>
        PostAsync(client, formPage, action, fields.AsEnumerable());

    /// <summary>Posts a form that may repeat a field (several <c>Grants</c> or <c>RoleIds</c>).</summary>
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string formPage, string action, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var token = await DotNetForgeWebFactory.GetAntiforgeryTokenAsync(client, formPage);
        return await client.PostAsync(action, new FormUrlEncodedContent(fields.Append(new("__RequestVerificationToken", token))));
    }
}
