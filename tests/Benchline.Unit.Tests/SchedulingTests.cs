using Benchline.Application;
using Benchline.Domain;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Benchline.Unit.Tests;

public sealed class SchedulingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 9, 0, 0, TimeSpan.Zero);
    private static readonly Actor Member = new("m1", Role.Member);
    private static readonly Actor Technician = new("t1", Role.Technician);
    private static readonly Actor Coordinator = new("c1", Role.Coordinator);
    private static readonly Station Primary = new(Guid.NewGuid(), "Main", "Laser", "Room 1", StationStatus.Online);
    private static readonly Station Backup = new(Guid.NewGuid(), "Backup", "Laser", "Room 2", StationStatus.Online);
    private static readonly Slot OriginalA = new(Guid.NewGuid(), Primary.Id, Now.AddHours(1));
    private static readonly Slot OriginalB = new(Guid.NewGuid(), Primary.Id, Now.AddHours(2));
    private static readonly Slot Early = new(Guid.NewGuid(), Backup.Id, Now.AddHours(2));
    private static readonly Slot Late = new(Guid.NewGuid(), Backup.Id, Now.AddHours(3));
    private static readonly Booking Flexible = new(Guid.NewGuid(), Member.Id, OriginalA.Id, Late.End, BookingStatus.Reserved);
    private static readonly Booking Tight = new(Guid.NewGuid(), "m2", OriginalB.Id, Early.End, BookingStatus.Reserved);

    private static Schedule Baseline => new(1, [Primary, Backup], [OriginalA, OriginalB, Early, Late], [Flexible, Tight]);

    [Fact]
    public void RecoveryReassignsAFlexibleBookingToMakeRoomForATightDeadline()
    {
        var outage = Disable();
        outage.Bookings.Should().OnlyContain(booking => booking.Status == BookingStatus.Displaced);
        var plan = Preview(outage);

        plan.Unmatched.Should().BeEmpty();
        plan.Moves.Should().Equal(new Assignment(Flexible.Id, Late.Id), new Assignment(Tight.Id, Early.Id));

        var result = ScheduleRules.Execute(outage, Coordinator,
            new ScheduleCommand.Publish(plan.Revision, Primary.Id, plan.Moves), Now);

        var next = ((Result<ScheduleChange>.Ok)result).Value;
        next.Schedule.Revision.Should().Be(3);
        next.Schedule.Bookings.Should().OnlyContain(booking => booking.Status == BookingStatus.Reserved);
        next.Event.Should().Be(new ScheduleEvent.Recovered(Primary.Id, 2));
        outage.Bookings[0].SlotId.Should().Be(OriginalA.Id);
    }

    [Fact]
    public void OccupiedAndIncompatibleStationsDoNotAppearInAPlan()
    {
        var other = Backup with { Id = Guid.NewGuid(), Capability = "Printing" };
        var outage = Disable();
        var schedule = outage with
        {
            Stations = outage.Stations.Add(other),
            Slots = outage.Slots.Add(Early with { Id = Guid.NewGuid(), StationId = other.Id }),
            Bookings = outage.Bookings.Add(Flexible with { Id = Guid.NewGuid(), SlotId = Early.Id })
        };

        var plan = Preview(schedule);
        plan.Moves.Should().Equal(new Assignment(Flexible.Id, Late.Id));
        plan.Unmatched.Should().Equal(Tight.Id);
    }

    [Fact]
    public void PreviewAndPublishEnforceRolesAndRevision()
    {
        var outage = Disable();
        var plan = Preview(outage);
        ScheduleRules.Preview(outage, Technician, Primary.Id, Now).Should()
            .Be(new Result<RecoveryPlan>.Err(ScheduleRules.Forbidden));
        FaultOf(outage, Technician, new ScheduleCommand.Publish(2, Primary.Id, plan.Moves))
            .Should().Be(ScheduleRules.Forbidden);
        FaultOf(outage, Coordinator, new ScheduleCommand.Publish(1, Primary.Id, plan.Moves))
            .Should().Be(ScheduleRules.Stale);
        FaultOf(outage, Coordinator, new ScheduleCommand.Publish(2, Primary.Id, [new(Flexible.Id, Early.Id)]))
            .Code.Should().Be("changed-plan");
    }

    [Fact]
    public void ReservationValidatesCapacityDeadlineAndOwnership()
    {
        var reservation = new ScheduleCommand.Reserve(1, Guid.NewGuid(), Early.Id, Early.End);
        FaultOf(Baseline, Member, reservation with { SlotId = OriginalB.Id }).Code.Should().Be("slot-unavailable");
        FaultOf(Baseline, Member, reservation with { Deadline = Early.Start }).Code.Should().Be("invalid-deadline");
        FaultOf(Baseline, Member, reservation with { SlotId = Guid.Empty }).Code.Should().Be("slot-missing");
        FaultOf(Baseline, Member, new ScheduleCommand.Cancel(1, Tight.Id)).Code.Should().Be("booking-missing");

        var reserved = ((Result<ScheduleChange>.Ok)ScheduleRules.Execute(Baseline, Member, reservation, Now)).Value;
        var cancelled = ((Result<ScheduleChange>.Ok)ScheduleRules.Execute(reserved.Schedule, Member,
            new ScheduleCommand.Cancel(2, reservation.BookingId), Now)).Value;
        cancelled.Schedule.Bookings.Last().Status.Should().Be(BookingStatus.Cancelled);
        FaultOf(cancelled.Schedule, Member, new ScheduleCommand.Cancel(3, reservation.BookingId))
            .Code.Should().Be("booking-closed");
    }

    [Fact]
    public void RestorationLeavesDisplacedBookingsForStaffReview()
    {
        var outage = Disable();
        FaultOf(outage, Technician, new ScheduleCommand.SetAvailability(2, Primary.Id, false)).Code.Should().Be("same-status");
        var restored = ((Result<ScheduleChange>.Ok)ScheduleRules.Execute(outage, Technician,
            new ScheduleCommand.SetAvailability(2, Primary.Id, true), Now)).Value;
        restored.Schedule.Stations[0].Status.Should().Be(StationStatus.Online);
        restored.Schedule.Bookings.Should().OnlyContain(booking => booking.Status == BookingStatus.Displaced);
    }

    [Fact]
    public async Task HandlerPersistsAuditAndReportsAConcurrentWrite()
    {
        var repository = Substitute.For<IScheduleRepository>();
        var clock = Substitute.For<TimeProvider>();
        clock.GetUtcNow().Returns(Now);
        repository.Read(default).Returns(Baseline);
        repository.TryWrite(Arg.Any<Schedule>(), Arg.Any<Schedule>(), Arg.Any<AuditEntry>(), default)
            .Returns(true, false);
        var workshop = new Workshop(repository, clock);
        var command = new ScheduleCommand.Reserve(1, Guid.NewGuid(), Early.Id, Early.End);

        (await workshop.Execute(Member, command, default)).Should().Be(new Result<long>.Ok(2));
        (await workshop.Execute(Member, command, default)).Should().Be(new Result<long>.Err(ScheduleRules.Stale));
        (await workshop.Execute(Technician, command, default)).Should().Be(new Result<long>.Err(ScheduleRules.Forbidden));
        await repository.Received(2).TryWrite(Arg.Is<Schedule>(item => item.Revision == 1),
            Arg.Is<Schedule>(item => item.Revision == 2),
            Arg.Is<AuditEntry>(item => item.ActorId == Member.Id && item.Event == "Reserved" && item.RecordedAt == Now),
            default);
    }

    private static Schedule Disable() => ((Result<ScheduleChange>.Ok)ScheduleRules.Execute(Baseline, Technician,
        new ScheduleCommand.SetAvailability(1, Primary.Id, false), Now)).Value.Schedule;

    private static RecoveryPlan Preview(Schedule state) =>
        ((Result<RecoveryPlan>.Ok)ScheduleRules.Preview(state, Coordinator, Primary.Id, Now)).Value;

    private static Fault FaultOf(Schedule state, Actor actor, ScheduleCommand command) =>
        ((Result<ScheduleChange>.Err)ScheduleRules.Execute(state, actor, command, Now)).Fault;
}
