namespace Benchline.Api;

internal sealed class DemoClock(DateTimeOffset time) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => time;
}
