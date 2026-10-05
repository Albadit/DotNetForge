using DotNetForge.Core.Authorization;
using DotNetForge.Shared.Constants;
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
}
