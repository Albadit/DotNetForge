using DotNetForge.Data;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The content-page rules owned by <see cref="PageService"/> (.docs/features/content-pages-and-routing.md), against a
/// real (in-memory SQLite) database.
/// </summary>
public sealed class PageServiceTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private SqliteConnection _connection = null!;
    private DotNetForgeDbContext _db = null!;
    private PageService _service = null!;

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();
        // EF Core counts its internal service providers per process; when the suite runs on MongoDB (one provider per
        // test database) that count is already past EF's limit of 20, so log the warning instead of throwing.
        _db = new DotNetForgeDbContext(new DbContextOptionsBuilder<DotNetForgeDbContext>()
            .UseSqlite(_connection)
            .ConfigureWarnings(w => w.Log(CoreEventId.ManyServiceProvidersCreatedWarning))
            .Options);
        await _db.Database.EnsureCreatedAsync();
        _service = new PageService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private async Task<Page> AddAsync(string slug, Guid? parent = null)
    {
        var page = new Page { TenantId = _tenant, Title = slug, Slug = slug, ParentPageId = parent };
        _db.Pages.Add(page);
        await _db.SaveChangesAsync();
        return page;
    }

    private Task<string?> ApplyAsync(Page page, string slug, Guid? parent = null) =>
        _service.ApplyAsync(page, new PageInput { Title = "T", Slug = slug, ParentPageId = parent }, _tenant);

    [Fact]
    public async Task Slugifies_and_applies_valid_input()
    {
        var page = new Page { TenantId = _tenant };
        Assert.Null(await ApplyAsync(page, "Our Team!"));
        Assert.Equal("our-team", page.Slug);
    }

    [Fact]
    public async Task Rejects_duplicate_slug_under_the_same_parent_including_root()
    {
        await AddAsync("about");
        var error = await ApplyAsync(new Page { TenantId = _tenant }, "About");
        Assert.Contains("already exists", error);
    }

    [Fact]
    public async Task Rejects_a_parent_that_creates_a_cycle()
    {
        var parent = await AddAsync("parent");
        var child = await AddAsync("child", parent.Id);
        var error = await ApplyAsync(parent, "parent", child.Id);
        Assert.Contains("cycle", error);
    }

    [Fact]
    public async Task Rejects_a_second_dynamic_segment_under_one_parent()
    {
        var blog = await AddAsync("blog");
        await AddAsync("[slug]", blog.Id);
        Assert.Contains("dynamic", await ApplyAsync(new Page { TenantId = _tenant }, "[id]", blog.Id));
    }

    [Fact]
    public async Task Rejects_overlong_fields_before_saving()
    {
        var error = await _service.ApplyAsync(new Page { TenantId = _tenant },
            new PageInput { Title = new string('x', 301), Slug = "x" }, _tenant);
        Assert.Contains("at most 300", error);
    }

    [Fact]
    public async Task Reorder_rejects_moves_that_duplicate_a_slug()
    {
        var a = await AddAsync("a");
        var b = await AddAsync("b");
        await AddAsync("team", a.Id);
        var otherTeam = await AddAsync("team", b.Id);

        var error = await _service.ReorderAsync(new[] { new PagePosition(otherTeam.Id, a.Id, 1) }, _tenant);
        Assert.Contains("share the slug 'team'", error);
    }

    [Fact]
    public async Task Reorder_rejects_pages_of_another_tenant()
    {
        var foreign = new Page { TenantId = Guid.NewGuid(), Title = "x", Slug = "x" };
        _db.Pages.Add(foreign);
        await _db.SaveChangesAsync();

        var error = await _service.ReorderAsync(new[] { new PagePosition(foreign.Id, null, 0) }, _tenant);
        Assert.Contains("does not exist", error);
    }

    [Fact]
    public async Task Delete_reparents_children_or_refuses_when_slugs_would_clash()
    {
        var about = await AddAsync("about");
        var team = await AddAsync("team", about.Id);
        Assert.Null(await _service.DeleteAsync(about));
        await _db.SaveChangesAsync();
        Assert.Null((await _db.Pages.SingleAsync(p => p.Id == team.Id)).ParentPageId);

        var news = await AddAsync("news");
        await AddAsync("team", news.Id); // would collide with the root-level "team"
        Assert.Contains("clash", await _service.DeleteAsync(news));
    }
}
