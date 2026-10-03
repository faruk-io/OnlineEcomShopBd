namespace TechBazar.UnitTests.Support;

/// <summary>Real time plus an adjustable offset, so token expiry / hourly throttles can be tested instantly.</summary>
public sealed class MutableClock : TimeProvider
{
    private TimeSpan _offset = TimeSpan.Zero;
    public void Advance(TimeSpan by) => _offset += by;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + _offset;
}
