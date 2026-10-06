using DotNetForge.Shared.Entities;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ValueGeneration;

namespace DotNetForge.Data.Database.Providers.MongoDb;

/// <summary>
/// The CMS model on MongoDB (.docs/database/mongodb.md). MongoDB has no migrations - the schema (collections and the
/// model's unique indexes) is created by <c>EnsureCreated</c> - and it never generates integer keys, so the two
/// integer-keyed entities get application-side generators.
/// </summary>
public sealed class MongoDbContext : DotNetForgeDbContext
{
    public MongoDbContext(DbContextOptions<MongoDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<AuditLogEntry>().Property(a => a.Id).HasValueGenerator<MongoLongKeyGenerator>();
        b.Entity<DataProtectionKey>().Property(k => k.Id).HasValueGenerator<MongoIntKeyGenerator>();
    }
}

/// <summary>
/// Keys for collections whose model uses integer identities. Long keys are time-ordered (milliseconds since 2020 in the
/// high bits, 20 random bits below), so audit entries still sort by creation; the chance of two instances colliding is
/// one in a million per entry created within the same millisecond.
/// </summary>
public static class MongoKeys
{
    private static readonly long Epoch = new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    public static long NextLong() => ((DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - Epoch) << 20) | (long)Random.Shared.Next(1 << 20);

    /// <summary>Random positive int: only the Data Protection key ring uses one (a few keys per year).</summary>
    public static int NextInt() => Random.Shared.Next(1, int.MaxValue);
}

public sealed class MongoLongKeyGenerator : ValueGenerator<long>
{
    public override bool GeneratesTemporaryValues => false;

    public override long Next(EntityEntry entry) => MongoKeys.NextLong();
}

public sealed class MongoIntKeyGenerator : ValueGenerator<int>
{
    public override bool GeneratesTemporaryValues => false;

    public override int Next(EntityEntry entry) => MongoKeys.NextInt();
}
