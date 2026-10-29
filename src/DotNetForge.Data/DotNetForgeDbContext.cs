using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// The CMS model (EF Core). The CMS database is PostgreSQL: it runs as <see cref="PostgreSqlDbContext"/>, which owns the
/// migrations (.docs/architecture/database.md). The public constructor serves tools and tests that only need the model.
/// </summary>
public class DotNetForgeDbContext : DbContext, IDataProtectionKeyContext
{
    public DotNetForgeDbContext(DbContextOptions<DotNetForgeDbContext> options) : base(options) { }

    /// <summary>For provider-specific subclasses that carry their own migration set (<see cref="PostgreSqlDbContext"/>).</summary>
    protected DotNetForgeDbContext(DbContextOptions options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<PagePermission> PagePermissions => Set<PagePermission>();
    public DbSet<PageUserPermission> PageUserPermissions => Set<PageUserPermission>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<MediaFolder> MediaFolders => Set<MediaFolder>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<InstalledExtension> InstalledExtensions => Set<InstalledExtension>();
    public DbSet<SystemState> SystemState => Set<SystemState>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuthProvider> AuthProviders => Set<AuthProvider>();
    public DbSet<UserMfaMethod> UserMfaMethods => Set<UserMfaMethod>();
    public DbSet<UserRecoveryCode> UserRecoveryCodes => Set<UserRecoveryCode>();
    public DbSet<ContentType> ContentTypes => Set<ContentType>();
    public DbSet<ContentEntry> ContentEntries => Set<ContentEntry>();
    public DbSet<ContentEntryValue> ContentEntryValues => Set<ContentEntryValue>();
    public DbSet<CustomEndpoint> CustomEndpoints => Set<CustomEndpoint>();
    public DbSet<CustomEndpointOperation> CustomEndpointOperations => Set<CustomEndpointOperation>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<TransferRecord> TransferRecords => Set<TransferRecord>();
    public DbSet<EmailLogEntry> EmailLog => Set<EmailLogEntry>();

    /// <summary>ASP.NET Core Data Protection key ring (auth cookies, antiforgery), shared by all instances.</summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);
            e.Property(u => u.PasswordHash).IsRequired();
            // Email is unique (.docs/features/authorization.md).
            e.HasIndex(u => u.Email).IsUnique();
            e.Ignore(u => u.DisplayName);
        });

        b.Entity<Role>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(100);
            e.Property(r => r.Description).HasMaxLength(500);
            e.HasIndex(r => r.Name).IsUnique();
            e.HasMany(r => r.Permissions).WithOne(p => p.Role!).HasForeignKey(p => p.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RolePermission>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Area).IsRequired().HasMaxLength(100);
            e.Property(p => p.Action).IsRequired().HasMaxLength(100);
            e.HasIndex(p => new { p.RoleId, p.Area, p.Action }).IsUnique();
        });

        b.Entity<UserRole>(e =>
        {
            e.HasKey(ur => new { ur.UserId, ur.RoleId });
            e.HasOne(ur => ur.User).WithMany(u => u.UserRoles).HasForeignKey(ur => ur.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ur => ur.Role).WithMany(r => r.UserRoles).HasForeignKey(ur => ur.RoleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Page>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Slug).IsRequired().HasMaxLength(200);
            e.Property(p => p.Title).IsRequired().HasMaxLength(300);
            e.Property(p => p.MetaTitle).HasMaxLength(300);
            e.Property(p => p.MetaDescription).HasMaxLength(1000);
            e.Property(p => p.TargetUrl).HasMaxLength(2000);
            e.Property(p => p.SeoKeywords).HasMaxLength(500);
            e.Property(p => p.CanonicalUrl).HasMaxLength(2000);
            e.Property(p => p.FileReference).HasMaxLength(2000);
            e.Property(p => p.ThemeId).HasMaxLength(100);
            e.Property(p => p.SocialImageUrl).HasMaxLength(2000);
            e.HasIndex(p => new { p.ParentPageId, p.Slug }).IsUnique();
        });

        b.Entity<PagePermission>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Permission).IsRequired().HasMaxLength(20);
            e.HasIndex(p => new { p.PageId, p.RoleId, p.Permission }).IsUnique();
        });

        b.Entity<PageUserPermission>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Permission).IsRequired().HasMaxLength(20);
            e.HasIndex(p => new { p.PageId, p.UserId, p.Permission }).IsUnique();
        });

        b.Entity<MediaFile>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.FileName).IsRequired().HasMaxLength(400);
            e.Property(m => m.OriginalName).HasMaxLength(400);
            e.Property(m => m.ContentType).HasMaxLength(200);
            e.Property(m => m.RelativePath).IsRequired().HasMaxLength(1000);
            e.HasIndex(m => m.FolderId);
            e.HasIndex(m => m.Optimization);
        });

        b.Entity<MediaFolder>(e =>
        {
            e.HasKey(f => f.Id);
            e.Property(f => f.Name).IsRequired().HasMaxLength(100);
            e.Property(f => f.Path).IsRequired().HasMaxLength(1000);
            e.HasIndex(f => f.Path).IsUnique();
            e.HasIndex(f => f.ParentId);
        });

        b.Entity<ApiToken>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired().HasMaxLength(200);
            e.Property(t => t.Description).HasMaxLength(1000);
            e.Property(t => t.TokenHash).IsRequired();
            e.Property(t => t.TokenPrefix).IsRequired().HasMaxLength(64);
            e.Property(t => t.PermissionsCsv).HasMaxLength(2000);
            e.HasIndex(t => t.TokenPrefix);
            e.HasIndex(t => t.Name).IsUnique();
            e.Ignore(t => t.Permissions);
        });

        b.Entity<Webhook>(e =>
        {
            e.HasKey(w => w.Id);
            e.Property(w => w.Name).IsRequired().HasMaxLength(200);
            e.Property(w => w.Url).IsRequired().HasMaxLength(2000);
            e.Property(w => w.Secret).IsRequired();
            e.Property(w => w.EventsCsv).HasMaxLength(1000);
            e.Property(w => w.HeadersJson).HasMaxLength(4000);
            e.HasMany(w => w.Deliveries).WithOne(d => d.Webhook!).HasForeignKey(d => d.WebhookId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Ignore(w => w.Events);
        });

        b.Entity<WebhookDelivery>(e =>
        {
            e.HasKey(d => d.Id);
            e.Property(d => d.TargetUrl).HasMaxLength(2000);
            e.Property(d => d.Event).HasMaxLength(100);
            e.Property(d => d.Detail).HasMaxLength(4000);
            e.Property(d => d.RequestBody).HasMaxLength(65536);
            e.Property(d => d.ResponseBody).HasMaxLength(4000);
            e.HasIndex(d => d.WebhookId);
            // The delivery worker's query: due pending attempts.
            e.HasIndex(d => new { d.Outcome, d.NextAttemptDate });
        });

        b.Entity<AuditLogEntry>(e =>
        {
            e.HasKey(a => a.Id);
            e.Property(a => a.Id).ValueGeneratedOnAdd();
            e.Property(a => a.Action).IsRequired().HasMaxLength(100);
            e.Property(a => a.EntityType).HasMaxLength(100);
            e.Property(a => a.EntityId).HasMaxLength(100);
            e.Property(a => a.UserDisplaySnapshot).HasMaxLength(256);
            e.Property(a => a.EntityDisplaySnapshot).HasMaxLength(400);
            e.Property(a => a.IpAddress).HasMaxLength(64);
            e.Property(a => a.UserAgent).HasMaxLength(512);
            e.HasIndex(a => a.Timestamp);
            e.HasIndex(a => a.Action);
        });

        b.Entity<InstalledExtension>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(200);
            e.Property(x => x.Name).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Version).IsRequired().HasMaxLength(50);
            e.Property(x => x.Author).HasMaxLength(200);
            e.Property(x => x.PermissionsCsv).HasMaxLength(2000);
        });

        b.Entity<SystemState>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.CmsVersion).HasMaxLength(50);
        });

        // Structured content (.docs/features/structured-content.md): types, entries and the value index.
        b.Entity<ContentType>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired().HasMaxLength(100);
            e.Property(t => t.Slug).IsRequired().HasMaxLength(64);
            e.Property(t => t.Description).HasMaxLength(500);
            e.Property(t => t.Icon).IsRequired().HasMaxLength(40);
            e.Property(t => t.FieldsJson).IsRequired();
            e.Property(t => t.TitleField).HasMaxLength(64);
            e.HasIndex(t => t.Slug).IsUnique();
        });

        b.Entity<ContentEntry>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired().HasMaxLength(300);
            e.Property(x => x.SearchText).IsRequired().HasMaxLength(1000);
            e.Property(x => x.DataJson).IsRequired();
            e.HasIndex(x => new { x.ContentTypeId, x.SortOrder });
        });

        // Custom API endpoints (.docs/features/custom-endpoints.md).
        // Customized email templates only (.docs/pages/email-templates.md); the defaults live in code.
        b.Entity<EmailTemplate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Key).IsRequired().HasMaxLength(100);
            e.Property(x => x.Subject).IsRequired().HasMaxLength(500);
            e.Property(x => x.Body).IsRequired();
            e.HasIndex(x => x.Key).IsUnique();
        });

        b.Entity<EmailLogEntry>(e =>
        {
            e.ToTable("EmailLog");
            e.HasKey(x => x.Id);
            e.Property(x => x.Recipient).IsRequired().HasMaxLength(320);
            e.Property(x => x.Subject).IsRequired().HasMaxLength(500);
            e.Property(x => x.Template).HasMaxLength(100);
            e.Property(x => x.Error).HasMaxLength(300);
            e.HasIndex(x => x.SentAt);
        });

        b.Entity<TransferRecord>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Provider).IsRequired().HasMaxLength(50);
            e.Property(x => x.SourceProvider).HasMaxLength(50);
            e.Property(x => x.FileName).IsRequired().HasMaxLength(260);
            e.Property(x => x.StorageKey).HasMaxLength(500);
            e.Property(x => x.Checksum).HasMaxLength(64);
            e.Property(x => x.Error).HasMaxLength(2000);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasIndex(x => x.StartedAt);
        });

        b.Entity<CustomEndpoint>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).IsRequired().HasMaxLength(100);
            e.Property(x => x.Path).IsRequired().HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(500);
            e.Property(x => x.Database).HasMaxLength(64);
            e.HasIndex(x => x.Path);
        });

        b.Entity<CustomEndpointOperation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Method).IsRequired().HasMaxLength(10);
            e.Property(x => x.Path).IsRequired().HasMaxLength(200);
            e.Property(x => x.Sql).IsRequired();
            e.Property(x => x.ParametersJson).IsRequired();
            e.HasIndex(x => new { x.EndpointId, x.Method, x.Path }).IsUnique();
        });

        b.Entity<ContentEntryValue>(e =>
        {
            e.HasKey(v => v.Id);
            e.Property(v => v.Field).IsRequired().HasMaxLength(100);
            e.Property(v => v.Text).HasMaxLength(200);
            e.HasIndex(v => new { v.ContentTypeId, v.Published, v.Field, v.Text });
            e.HasIndex(v => new { v.ContentTypeId, v.Published, v.Field, v.Number });
            e.HasIndex(v => v.EntryId);
        });

        b.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Key).IsRequired().HasMaxLength(200);
            e.Property(s => s.Value).HasMaxLength(4000);
            e.HasIndex(s => s.Key).IsUnique();
        });

        b.Entity<AuthProvider>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Key).IsRequired().HasMaxLength(50);
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
            e.Property(p => p.SettingsJson).HasMaxLength(8000);
            e.HasIndex(p => p.Name).IsUnique();
            e.HasIndex(p => p.Key).IsUnique();
        });

        // Second sign-in factors (.docs/features/mfa.md); they go with the user.
        b.Entity<UserMfaMethod>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.Name).IsRequired().HasMaxLength(100);
            e.Property(m => m.Secret).HasMaxLength(1000);
            e.Property(m => m.PhoneNumber).HasMaxLength(20);
            e.Property(m => m.CredentialId).HasMaxLength(1024);
            e.Property(m => m.PublicKey).HasMaxLength(2048);
            e.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => m.UserId);
            e.HasIndex(m => m.CredentialId);
        });

        b.Entity<UserRecoveryCode>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.CodeHash).IsRequired().HasMaxLength(64);
            e.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => c.UserId);
        });
    }
}
