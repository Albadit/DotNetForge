using DotNetForge.Core.Authorization;
using DotNetForge.Shared.Authorization;
using DotNetForge.Shared.Constants;
using DotNetForge.Shared.Stores;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Verifies the default Permission Matrix is enforced by <see cref="PermissionService.Has"/>
/// (.docs/features/authorization.md, .docs/guides/testing.md).
/// </summary>
public sealed class PermissionTests
{
    private readonly PermissionService _permissions = new();

    [Theory]
    [InlineData(PermissionAreas.Extensions, PermissionActions.Manage)]
    [InlineData(PermissionAreas.Users, PermissionActions.Delete)]
    [InlineData(PermissionAreas.Updates, PermissionActions.Install)]
    [InlineData(PermissionAreas.Webhooks, PermissionActions.Create)]
    public void SuperAdmin_has_everything(string area, string action) =>
        Assert.True(_permissions.Has(Roles.SuperAdmin, area, action));

    [Fact]
    public void Public_cannot_delete_users() =>
        Assert.False(_permissions.Has(Roles.Public, PermissionAreas.Users, PermissionActions.Delete));

    [Fact]
    public void Public_can_read_content() =>
        Assert.True(_permissions.Has(Roles.Public, PermissionAreas.CollectionTypes, PermissionActions.Read));

    [Fact]
    public void Public_cannot_create_content() =>
        Assert.False(_permissions.Has(Roles.Public, PermissionAreas.CollectionTypes, PermissionActions.Create));

    [Fact]
    public void Editor_can_publish_but_cannot_manage_users()
    {
        Assert.True(_permissions.Has(Roles.Editor, PermissionAreas.CollectionTypes, PermissionActions.Publish));
        Assert.False(_permissions.Has(Roles.Editor, PermissionAreas.Users, PermissionActions.Create));
    }

    [Fact]
    public void Author_is_limited_to_own_content()
    {
        Assert.True(_permissions.Has(Roles.Author, PermissionAreas.CollectionTypes, PermissionActions.UpdateOwn));
        Assert.False(_permissions.Has(Roles.Author, PermissionAreas.CollectionTypes, PermissionActions.Publish));
        Assert.False(_permissions.Has(Roles.Author, PermissionAreas.CollectionTypes, PermissionActions.Update));
    }

    [Fact]
    public void Admin_manages_users_but_not_extensions()
    {
        Assert.True(_permissions.Has(Roles.Admin, PermissionAreas.Users, PermissionActions.Create));
        Assert.True(_permissions.Has(Roles.Admin, PermissionAreas.Webhooks, PermissionActions.Manage));
        Assert.False(_permissions.Has(Roles.Admin, PermissionAreas.Extensions, PermissionActions.Manage));
        Assert.False(_permissions.Has(Roles.Admin, PermissionAreas.Updates, PermissionActions.Install));
    }

    [Fact]
    public void HasAny_is_union_of_roles()
    {
        var roles = new[] { Roles.Author, Roles.Editor };
        // Editor grants publish even though Author does not.
        Assert.True(_permissions.HasAny(roles, PermissionAreas.CollectionTypes, PermissionActions.Publish));
    }

    [Fact]
    public void Unknown_role_has_nothing() =>
        Assert.False(_permissions.Has("Ghost", PermissionAreas.CollectionTypes, PermissionActions.Read));

    [Fact]
    public void Tenant_checks_use_the_stored_grants_of_each_role()
    {
        var tenant = Guid.NewGuid();
        var service = new PermissionService(new FixedGrants(new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Reviewer"] = new HashSet<string> { PermissionCatalog.Key(PermissionAreas.CollectionTypes, PermissionActions.Read) },
            [Roles.Editor] = new HashSet<string>(),
        }));

        // A custom role has exactly its grants; an edited built-in role loses what was removed.
        Assert.True(service.HasAny(tenant, new[] { "Reviewer" }, PermissionAreas.CollectionTypes, PermissionActions.Read));
        Assert.False(service.HasAny(tenant, new[] { "Reviewer" }, PermissionAreas.Settings, PermissionActions.Read));
        Assert.False(service.HasAny(tenant, new[] { Roles.Editor }, PermissionAreas.CollectionTypes, PermissionActions.Read));
        // Super Admin always has everything; a role the tenant doesn't have falls back to the matrix.
        Assert.True(service.HasAny(tenant, new[] { Roles.SuperAdmin }, PermissionAreas.Updates, PermissionActions.Install));
        Assert.True(service.HasAny(tenant, new[] { Roles.Author }, PermissionAreas.Media, PermissionActions.Create));
    }

    private sealed class FixedGrants(IReadOnlyDictionary<string, IReadOnlySet<string>> grants) : IRoleGrantStore
    {
        public IReadOnlyDictionary<string, IReadOnlySet<string>> GetGrants(Guid tenantId) => grants;

        public void Invalidate(Guid tenantId)
        {
        }
    }
}
