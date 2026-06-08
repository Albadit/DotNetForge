using DotNetForge.Abstractions.Security;

namespace DotNetForge.Infrastructure.Security;

/// <summary>Real-time <see cref="IDateTimeProvider"/> backed by the system clock (UTC).</summary>
public sealed class SystemClock : IDateTimeProvider
{
    public DateTime UtcNow => DateTime.UtcNow;
}
