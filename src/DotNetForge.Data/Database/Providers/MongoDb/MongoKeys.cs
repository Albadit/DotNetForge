namespace DotNetForge.Data.Database.Providers.MongoDb;

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
