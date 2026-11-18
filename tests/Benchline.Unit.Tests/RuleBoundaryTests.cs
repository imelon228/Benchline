using Benchline.Domain;
using Xunit;

namespace Benchline.Unit.Tests;

public sealed class RuleBoundaryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddYears(50);
    private static readonly Actor Member = new("member", Role.Member);
    private static readonly Actor Coordinator = new("coord", Role.Coordinator);
    private static readonly Station Primary = new(Guid.NewGuid(), "Primary", "Laser", "A", StationStatus.OutOfService);
    private static readonly Station Backup = new(Guid.NewGuid(), "Backup", "Laser", "B", StationStatus.Online);
    private static readonly Slot Original = new(Guid.NewGuid(), Primary.Id, Now);
    private static readonly Slot Later = new(Guid.NewGuid(), Backup.Id, Now.AddHours(1));

    [Theory]
    [InlineData(BookingStatus.Reserved, 2, "member", "booking-closed")]
    [InlineData(BookingStatus.Displaced, 0, "member", "booking-closed")]
    [InlineData(BookingStatus.Displaced, 1, "different-owner", "booking-missing")]
    [InlineData(BookingStatus.Cancelled, 1, "member", "booking-closed")]
    public void CancellationChecksOwnerAndTiming(BookingStatus status, int deadlineHours, string owner, string fault)
    {
        var booking = new Booking(Guid.NewGuid(), owner, Original.Id, Now.AddHours(deadlineHours), status);
        var state = new Schedule(3, [Primary], [Original], [booking]);
        var result = ScheduleRules.Execute(state, Member, new ScheduleCommand.Cancel(3, booking.Id), Now);
        Assert.Equal(fault, Assert.IsType<Result<ScheduleChange>.Err>(result).Fault.Code);
    }

    [Fact]
    public void ReplacementsRespectTheExactDeadlineAndEmptyPlanCannotBePublished()
    {
        var expired = new Booking(Guid.NewGuid(), "expired", Original.Id, Later.End.AddTicks(-1), BookingStatus.Displaced);
        var exact = new Booking(Guid.NewGuid(), "exact", Original.Id, Later.End, BookingStatus.Displaced);
        var state = new Schedule(3, [Primary, Backup], [Original, Later], [expired, exact]);
        var plan = RecoveryPlanner.Build(state, Primary, Now);
        Assert.Equal(exact.Id, Assert.Single(plan.Moves).BookingId);
        Assert.Equal(expired.Id, Assert.Single(plan.Unmatched));

        var empty = ScheduleRules.Execute(state with { Bookings = [expired] }, Coordinator,
            new ScheduleCommand.Publish(3, Primary.Id, []), Now);
        Assert.Equal("no-replacements", Assert.IsType<Result<ScheduleChange>.Err>(empty).Fault.Code);
    }

    [Fact]
    public void EqualDeadlinesMatchDeterministicallyAcrossStationsAndInputOrder()
    {
        var secondBackup = Backup with { Id = Guid.NewGuid(), Name = "Other backup" };
        var alternate = Later with { Id = Guid.NewGuid(), StationId = secondBackup.Id };
        var first = new Booking(Guid.NewGuid(), "first", Original.Id, Later.End, BookingStatus.Displaced);
        var second = new Booking(Guid.NewGuid(), "second", Original.Id, Later.End, BookingStatus.Displaced);
        var state = new Schedule(3, [Primary, Backup, secondBackup], [Original, Later, alternate], [first, second]);
        var plan = RecoveryPlanner.Build(state, Primary, Now);
        var shuffled = RecoveryPlanner.Build(state with { Bookings = [second, first], Slots = [alternate, Original, Later] }, Primary, Now);

        Assert.Equal(2, plan.Moves.Length);
        Assert.Equal(plan.Moves.ToArray(), shuffled.Moves.ToArray());
        Assert.Empty(plan.Unmatched);
    }
}
