using DotNetForge.Core.Installation;
using DotNetForge.Infrastructure.Security;
using DotNetForge.Shared.Dtos;
using DotNetForge.Shared.Entities;
using DotNetForge.Shared.Results;
using DotNetForge.Shared.Stores;
using Xunit;

namespace DotNetForge.Tests;

/// <summary>
/// Verifies setup-wizard validation and the create-first-admin orchestration (installation_setup.md),
/// using an in-memory installation store and the real password hasher.
/// </summary>
public sealed class InstallationServiceTests
{
    private sealed class FakeInstallationStore : IInstallationStore
    {
        public bool Installed { get; private set; }
        public User? CapturedAdmin { get; private set; }

        public Task<bool> IsInstalledAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Installed);

        public Task<Result> InstallFirstAdminAsync(User admin, CancellationToken cancellationToken = default)
        {
            if (Installed)
            {
                return Task.FromResult(Result.Fail("The CMS is already installed."));
            }

            CapturedAdmin = admin;
            Installed = true;
            return Task.FromResult(Result.Ok());
        }
    }

    private static InstallationService Build(FakeInstallationStore store) =>
        new(store, new Pbkdf2PasswordHasher());

    private static SetupRequest ValidRequest() => new()
    {
        FirstName = "Ada",
        Email = "admin@example.com",
        Password = "Sup3rSecret",
        ConfirmPassword = "Sup3rSecret",
    };

    [Fact]
    public async Task Valid_setup_creates_admin_and_marks_installed()
    {
        var store = new FakeInstallationStore();
        var result = await Build(store).InstallAsync(ValidRequest());

        Assert.True(result.Succeeded);
        Assert.True(store.Installed);
        Assert.NotNull(store.CapturedAdmin);
        Assert.Equal("admin@example.com", store.CapturedAdmin!.Email);
        Assert.NotEqual("Sup3rSecret", store.CapturedAdmin.PasswordHash); // stored as a hash
        Assert.True(new Pbkdf2PasswordHasher().Verify("Sup3rSecret", store.CapturedAdmin.PasswordHash));
    }

    [Fact]
    public async Task Password_mismatch_is_rejected()
    {
        var store = new FakeInstallationStore();
        var request = ValidRequest();
        request.ConfirmPassword = "Different1";

        var result = await Build(store).InstallAsync(request);

        Assert.True(result.Failed);
        Assert.False(store.Installed);
    }

    [Fact]
    public async Task Weak_password_is_rejected()
    {
        var store = new FakeInstallationStore();
        var request = ValidRequest();
        request.Password = "weak";
        request.ConfirmPassword = "weak";

        var result = await Build(store).InstallAsync(request);

        Assert.True(result.Failed);
        Assert.False(store.Installed);
    }

    [Fact]
    public async Task Invalid_email_is_rejected()
    {
        var store = new FakeInstallationStore();
        var request = ValidRequest();
        request.Email = "not-an-email";

        var result = await Build(store).InstallAsync(request);

        Assert.True(result.Failed);
        Assert.False(store.Installed);
    }

    [Fact]
    public async Task Already_installed_is_rejected()
    {
        var store = new FakeInstallationStore();
        await Build(store).InstallAsync(ValidRequest());          // first install
        var second = await Build(store).InstallAsync(ValidRequest());

        Assert.True(second.Failed);
    }
}
