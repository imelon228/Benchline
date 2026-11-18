using Benchline.Application;
using Benchline.Domain;
using NSubstitute;
using Xunit;

namespace Benchline.Unit.Tests;

public sealed class WorkshopUseCaseTests
{
    [Fact]
    public async Task UseCasesDeclareAuditEventsAndBoundPageSize()
    {
        var clock = Substitute.For<TimeProvider>();
        var now = new DateTimeOffset(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
        clock.GetUtcNow().Returns(now);
        var laser = new Station(Guid.NewGuid(), "Main", "Laser", "Shop", StationStatus.Online);
        var backup = laser with { Id = Guid.NewGuid(), Name = "Backup" };
        var session = new Slot(Guid.NewGuid(), laser.Id, now.AddHours(1));
        var replacement = new Slot(Guid.NewGuid(), backup.Id, now.AddHours(2));
        var booking = new Booking(Guid.NewGuid(), "m", session.Id, replacement.End, BookingStatus.Reserved);
        var state = new Schedule(2, [laser, backup], [session, replacement], [booking]);

        var repo = Substitute.For<IScheduleRepository>();
        repo.Read(default).Returns(_ => state);
        var events = new List<string>();
        repo.TryWrite(Arg.Any<Schedule>(), Arg.Any<Schedule>(), Arg.Any<AuditEntry>(), default)
            .Returns(call =>
            {
                state = call.ArgAt<Schedule>(1);
                events.Add(call.Arg<AuditEntry>().Event);
                return true;
            });
        var workshop = new Workshop(repo, clock);
        var tech = new Actor("t", Role.Technician);
        var coord = new Actor("c", Role.Coordinator);

        Assert.Same(state, await workshop.Read(default));
        Assert.IsType<Result<long>.Ok>(await workshop.Execute(tech, new ScheduleCommand.SetAvailability(2, laser.Id, false), default));
        var preview = Assert.IsType<Result<RecoveryPlan>.Ok>(await workshop.Preview(coord, laser.Id, default)).Value;
        Assert.IsType<Result<long>.Ok>(await workshop.Execute(coord, new ScheduleCommand.Publish(3, laser.Id, preview.Moves), default));
        Assert.IsType<Result<long>.Ok>(await workshop.Execute(tech, new ScheduleCommand.SetAvailability(4, laser.Id, true), default));
        Assert.IsType<Result<long>.Ok>(await workshop.Execute(new Actor("m", Role.Member), new ScheduleCommand.Cancel(5, booking.Id), default));

        await workshop.Audit(10, 200, default);
        await repo.Received().Audit(10, 100, default);
        Assert.Equal(new[] { "Station out of service", "Recovery published", "Station restored", "Cancelled" }, events);
    }

    [Fact]
    public void MapPreservesAFaultWithoutCallingItsFunction()
    {
        Result<int> failure = new Result<int>.Err(ScheduleRules.Stale);
        Result<int> success = new Result<int>.Ok(2);
        Assert.Equal(new Result<string>.Err(ScheduleRules.Stale), failure.Map<string>(_ => throw new InvalidOperationException()));
        Assert.Equal(new Result<string>.Ok("two"), success.Map(_ => "two"));
    }
}
