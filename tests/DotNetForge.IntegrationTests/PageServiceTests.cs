using DotNetForge.Data;
using DotNetForge.Data.Database.Providers.PostgreSql;
using DotNetForge.Shared.Content;
using DotNetForge.Shared.Entities;
using DotNetForge.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DotNetForge.IntegrationTests;

/// <summary>
/// The content-page rules owned by <see cref="PageService"/> (.docs/features/content-pages-and-routing.md), against a
/// real database (a fresh PostgreSQL database per test, <see cref="TestDatabaseServer"/>).
/// </summary>
public sealed class PageServiceTests : IAsyncLifetime
{
    private DotNetForgeDbContext _db = null!;
    private PageService _service = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<PostgreSqlDbContext>();
        TestDatabaseServer.Configure(options, TestDatabaseServer.CmsDatabase($"dnf_pages_{Guid.NewGuid():N}"));
        _db = new PostgreSqlDbContext(options.Options);
        await _db.Database.MigrateAsync();
        _service = new PageService(_db);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }

    private async Task<Page> AddAsync(string slug, Guid? parent = null)
    {
        var page = new Page { Title = slug, Slug = slug, ParentPageId = parent };
        _db.Pages.Add(page);
        await _db.SaveChangesAsync();
        return page;
    }

    [Fact]
    public async Task The_home_page_stays_the_root()
    {
        var home = new Page { Title = "Home", Slug = "/" };
        _db.Pages.Add(home);
        await _db.SaveChangesAsync();
        var about = await AddAsync("about");
        about.SortOrder = 1;
        await _db.SaveChangesAsync();

        // Its title and content change as any page's do; its place doesn't.
        Assert.Null(await _service.ApplyAsync(home, new PageInput { Title = "Welcome", Slug = "/" }));
        Assert.Equal("Welcome", home.Title);
        Assert.Equal(PageService.HomeIsFixed, await _service.ApplyAsync(home, new PageInput { Title = "T", Slug = "start" }));
        Assert.Equal(PageService.HomeIsFixed, await _service.ApplyAsync(home, new PageInput { Title = "T", Slug = "/", ParentPageId = about.Id }));
        Assert.Equal(PageService.HomeIsFixed, await _service.ApplyAsync(home, new PageInput { Title = "T", Slug = "/", SortOrder = 3 }));
        Assert.Equal(PageService.HomeIsFixed, await _service.MoveAsync(home.Id, about.Id));
        Assert.Equal(PageService.HomeIsFixed, await _service.ReorderAsync(new[] { new PagePosition(home.Id, null, 2) }));

        Assert.Equal(PageService.HomeCantBeDeleted, await _service.DeleteAsync(home));
        Assert.True(await _db.Pages.AnyAsync(p => p.Id == home.Id));

        // Nothing goes under it or before it, and the slug stays its own.
        Assert.Equal(PageService.NothingUnderHome, await _service.MoveAsync(about.Id, home.Id));
        Assert.Equal(PageService.NothingUnderHome, await ApplyAsync(about, "about", home.Id));
        Assert.Equal(PageService.NothingBeforeHome, await _service.ReorderAsync(new[] { new PagePosition(about.Id, null, 0) }));
        Assert.Equal(PageService.HomeIsFixed, await ApplyAsync(about, "/", about.Id));

        // A root page asking for 0 comes right after it.
        Assert.Null(await _service.ApplyAsync(about, new PageInput { Title = "About", Slug = "about", SortOrder = 0 }));
        Assert.Equal(1, about.SortOrder);
    }

    [Fact]
    public async Task No_page_goes_below_sort_order_0()
    {
        var parent = await AddAsync("parent");
        var child = await AddAsync("child", parent.Id);

        Assert.Equal(PageService.SortOrderTooLow, await _service.ApplyAsync(child, new PageInput { Title = "T", Slug = "child", ParentPageId = parent.Id, SortOrder = -1 }));
        Assert.Equal(PageService.SortOrderTooLow, await _service.ReorderAsync(new[] { new PagePosition(child.Id, parent.Id, -1) }));
        Assert.Null(await _service.ReorderAsync(new[] { new PagePosition(child.Id, parent.Id, 0) }));
    }

    [Fact]
    public async Task A_page_takes_its_children_along()
    {
        var about = await AddAsync("about");
        var team = await AddAsync("team", about.Id);
        var news = await AddAsync("news");

        Assert.Null(await _service.MoveAsync(about.Id, news.Id));
        await _db.SaveChangesAsync();

        Assert.Equal(news.Id, about.ParentPageId);
        Assert.Equal(about.Id, team.ParentPageId);
    }

    [Fact]
    public async Task Moving_puts_a_page_last_under_its_new_parent_or_at_the_root()
    {
        var blog = await AddAsync("blog");
        var first = await AddAsync("first", blog.Id);
        first.SortOrder = 4;
        await _db.SaveChangesAsync();
        var news = await AddAsync("news");

        Assert.Null(await _service.MoveAsync(news.Id, blog.Id));
        await _db.SaveChangesAsync();
        Assert.Equal(blog.Id, news.ParentPageId);
        Assert.Equal(5, news.SortOrder);

        Assert.Null(await _service.MoveAsync(news.Id, null));
        await _db.SaveChangesAsync();
        Assert.Null(news.ParentPageId);
        Assert.Equal(blog.SortOrder + 1, news.SortOrder);
    }

    [Fact]
    public async Task Moving_keeps_the_rules_of_the_tree()
    {
        var a = await AddAsync("a");
        var child = await AddAsync("child", a.Id);
        var b = await AddAsync("b");
        await AddAsync("team", a.Id);
        var otherTeam = await AddAsync("team", b.Id);

        Assert.Contains("cycle", await _service.MoveAsync(a.Id, child.Id));
        Assert.Contains("cycle", await _service.MoveAsync(a.Id, a.Id));
        Assert.Contains("share the slug 'team'", await _service.MoveAsync(otherTeam.Id, a.Id));
        Assert.Contains("does not exist", await _service.MoveAsync(a.Id, Guid.NewGuid()));
    }

    private Task<string?> ApplyAsync(Page page, string slug, Guid? parent = null) =>
        _service.ApplyAsync(page, new PageInput { Title = "T", Slug = slug, ParentPageId = parent });

    [Fact]
    public async Task Published_is_the_only_switch_and_saving_clears_the_legacy_disabled_flag()
    {
        // A page saved before the Disabled switch was removed: published but disabled, so not live.
        var page = await AddAsync("legacy");
        page.Published = true;
        page.Disabled = true;
        await _db.SaveChangesAsync();
        Assert.False(PageInput.From(page).Published); // the form shows it switched off ("Disabled")
        Assert.Empty(_db.Pages.Live(DateTime.UtcNow).Where(p => p.Id == page.Id));

        // Switching Published on makes it live and clears the old flag.
        Assert.Null(await _service.ApplyAsync(page, new PageInput { Title = "Legacy", Slug = "legacy", Published = true }));
        await _db.SaveChangesAsync();
        Assert.False(page.Disabled);
        Assert.Single(_db.Pages.Live(DateTime.UtcNow).Where(p => p.Id == page.Id));
    }

    [Fact]
    public async Task Slugifies_and_applies_valid_input()
    {
        var page = new Page();
        Assert.Null(await ApplyAsync(page, "Our Team!"));
        Assert.Equal("our-team", page.Slug);
    }

    [Fact]
    public async Task Rejects_duplicate_slug_under_the_same_parent_including_root()
    {
        await AddAsync("about");
        var error = await ApplyAsync(new Page(), "About");
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
        Assert.Contains("dynamic", await ApplyAsync(new Page(), "[id]", blog.Id));
    }

    [Fact]
    public async Task Rejects_overlong_fields_before_saving()
    {
        var error = await _service.ApplyAsync(new Page(),
            new PageInput { Title = new string('x', 301), Slug = "x" });
        Assert.Contains("at most 300", error);
    }

    [Fact]
    public async Task Reorder_rejects_moves_that_duplicate_a_slug()
    {
        var a = await AddAsync("a");
        var b = await AddAsync("b");
        await AddAsync("team", a.Id);
        var otherTeam = await AddAsync("team", b.Id);

        var error = await _service.ReorderAsync(new[] { new PagePosition(otherTeam.Id, a.Id, 1) });
        Assert.Contains("share the slug 'team'", error);
    }

    [Fact]
    public async Task Reorder_rejects_pages_that_do_not_exist()
    {
        var error = await _service.ReorderAsync(new[] { new PagePosition(Guid.NewGuid(), null, 0) });
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
