using Benchline.Api;
using Benchline.Contracts;
using Benchline.Domain;
using Xunit;

namespace Benchline.Integration.Tests;

public sealed class DemoScheduleTests
{
    [Fact]
    public void RenewalRequiresBothLaserBookingsToHaveValidReplacements()
    {
        var now = DateTimeOffset.UnixEpoch;
        var primary = StationFor(DemoCatalog.Primary);
        var backup = StationFor(DemoCatalog.Backup);
        var printer = StationFor(DemoCatalog.Printer);
        var mill = StationFor(DemoCatalog.Mill);
        var first = Session(primary, 2);
        var second = Session(primary, 3);
        var early = Session(backup, 3);
        var late = Session(backup, 4);
        var print = Session(printer, 3);
        Booking flexible = new(Guid.NewGuid(), "member", first.Id, late.End, BookingStatus.Reserved);
        Booking urgent = new(Guid.NewGuid(), "member2", second.Id, early.End, BookingStatus.Reserved);
        Booking printBooking = new(Guid.NewGuid(), "member", print.Id, print.End, BookingStatus.Reserved);
        Schedule schedule = new(1, [primary, backup, printer, mill],
            [first, second, early, late, print, Session(primary, 5), Session(printer, 5), Session(mill, 5)],
            [flexible, urgent, printBooking]);

        Assert.False(DemoSchedule.NeedsRenewal(schedule, now.AddHours(1)));
        Assert.True(DemoSchedule.NeedsRenewal(schedule, now.AddHours(2)));
        Assert.True(DemoSchedule.NeedsRenewal(schedule with { Slots = schedule.Slots.Remove(early) }, now));
        Assert.True(DemoSchedule.NeedsRenewal(schedule with { Bookings = schedule.Bookings.Remove(printBooking) }, now));

        Slot Session(Station station, int hour) => new(Guid.NewGuid(), station.Id, now.AddHours(hour));
    }

    private static Station StationFor(DemoCatalog.DemoStation station) =>
        new(Guid.NewGuid(), station.Name, station.Capability, station.Location, StationStatus.Online);
}
