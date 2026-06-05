using DotNetForge.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace DotNetForge.Data;

/// <summary>
/// The single EF Core context for DotNetForge CMS. Provider selection (SQLite default / PostgreSQL)
/// is configured by the host; all schema changes flow through migrations (architecture.md).
/// </summary>
public sealed class DotNetForgeDbContext : DbContext
{
    public DotNetForgeDbContext(DbContextOptions<DotNetForgeDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();
    public DbSet<Webhook> Webhooks => Set<Webhook>();
    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();
    public DbSet<AuditLogEntry> AuditLogs => Set<AuditLogEntry>();
    public DbSet<InstalledExtension> InstalledExtensions => Set<InstalledExtension>();
    public DbSet<SystemState> SystemState => Set<SystemState>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<AuthProvider> AuthProviders => Set<AuthProvider>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Tenant>(e =>
        {
            e.HasKey(t => t.Id);
            e.Property(t => t.Name).IsRequired().HasMaxLength(200);
            e.Property(t => t.Slug).IsRequired().HasMaxLength(100);
            e.HasIndex(t => t.Slug).IsUnique();
            e.Property(t => t.DefaultLocale).HasMaxLength(20);
            e.Property(t => t.DefaultTheme).HasMaxLength(200);
        });

        b.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.Property(u => u.Email).IsRequired().HasMaxLength(256);
            e.Property(u => u.FirstName).HasMaxLength(100);
            e.Property(u => u.LastName).HasMaxLength(100);
            e.Property(u => u.PasswordHash).IsRequired();
            // Email is unique within a tenant (user_roles_permissions.md).
            e.HasIndex(u => new { u.TenantId, u.Email }).IsUnique();
            e.Ignore(u => u.DisplayName);
        });

        b.Entity<Role>(e =>
        {
            e.HasKey(r => r.Id);
            e.Property(r => r.Name).IsRequired().HasMaxLength(100);
            e.Property(r => r.Description).HasMaxLength(500);
            e.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();
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
            e.HasIndex(p => new { p.TenantId, p.ParentPageId, p.Slug }).IsUnique();
        });

        b.Entity<MediaFile>(e =>
        {
            e.HasKey(m => m.Id);
            e.Property(m => m.FileName).IsRequired().HasMaxLength(400);
            e.Property(m => m.OriginalName).HasMaxLength(400);
            e.Property(m => m.ContentType).HasMaxLength(200);
            e.Property(m => m.RelativePath).IsRequired().HasMaxLength(1000);
            e.Property(m => m.FolderPath).HasMaxLength(1000);
            e.HasIndex(m => m.TenantId);
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
            e.HasIndex(t => new { t.TenantId, t.Name }).IsUnique();
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
            e.HasIndex(d => d.WebhookId);
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
            e.HasIndex(a => new { a.TenantId, a.Action });
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

        b.Entity<Setting>(e =>
        {
            e.HasKey(s => s.Id);
            e.Property(s => s.Key).IsRequired().HasMaxLength(200);
            e.Property(s => s.Value).HasMaxLength(4000);
            e.HasIndex(s => new { s.TenantId, s.Key }).IsUnique();
        });

        b.Entity<AuthProvider>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Name).IsRequired().HasMaxLength(100);
            e.Property(p => p.SettingsJson).HasMaxLength(4000);
            e.HasIndex(p => p.Name).IsUnique();
        });
    }
}
