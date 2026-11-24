namespace Deckside.Core.Time;

/// <summary>Source of "now". Everything that depends on today's date receives one, so tests and snapshots are stable.</summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

public sealed class SystemClock : IClock
{
    public static readonly SystemClock Instance = new();

    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

/// <summary>A clock that always returns the same instant (tests, snapshots, replaying a plan).</summary>
public sealed class FixedClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNow { get; } = utcNow.ToUniversalTime();
}
