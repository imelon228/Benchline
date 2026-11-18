using Benchline.Domain;
using Xunit;

namespace Benchline.Unit.Tests;

public sealed class UnsupportedCommandTests
{
    [Fact]
    public void UnsupportedCommandReturnsAnInvalidFaultBeforeCheckingItsRevision()
    {
        var schedule = new Schedule(8, [], [], []);
        var outcome = ScheduleRules.Execute(schedule, new Actor("member", Role.Member),
            new Unsupported(7), DateTimeOffset.UnixEpoch);

        var result = Assert.IsType<Result<ScheduleChange>.Err>(outcome);
        Assert.Equal(FaultKind.Invalid, result.Fault.Kind);
        Assert.Equal("unsupported-command", result.Fault.Code);
    }

    private sealed record Unsupported(long Expected) : ScheduleCommand(Expected);
}
