using Benchline.Contracts;
using Benchline.Domain;

namespace Benchline.Api;

public static class DemoSchedule
{
    public static bool NeedsRenewal(Schedule schedule, DateTimeOffset now)
    {
        var stations = schedule.Stations.Where(station => DemoCatalog.Stations.Any(demo => demo.Name == station.Name))
            .ToDictionary(station => station.Name);
        if (stations.Count != DemoCatalog.Stations.Count || stations.Values.Any(station => station.Status != StationStatus.Online))
            return true;

        var futureSlots = schedule.Slots.Where(slot => slot.Start > now).ToDictionary(slot => slot.Id);
        var reserved = schedule.Bookings.Where(booking => booking.Status == BookingStatus.Reserved)
            .ToDictionary(booking => booking.SlotId);
        var openStations = futureSlots.Values.Where(slot => !reserved.ContainsKey(slot.Id))
            .Select(slot => slot.StationId).ToHashSet();
        if (stations.Values.Any(station => !openStations.Contains(station.Id)))
            return true;

        var primary = stations[DemoCatalog.Primary.Name];
        var laserBookings = futureSlots.Values.Where(slot => slot.StationId == primary.Id)
            .Select(slot => reserved.GetValueOrDefault(slot.Id)).OfType<Booking>().ToArray();
        var owners = laserBookings.Select(booking => booking.OwnerId).ToHashSet();
        var hasPrintBooking = futureSlots.Values.Any(slot => slot.StationId == stations[DemoCatalog.Printer.Name].Id
            && reserved.GetValueOrDefault(slot.Id)?.OwnerId == "member");
        if (!owners.Contains("member") || !owners.Contains("member2") || !hasPrintBooking)
            return true;

        var outage = ScheduleRules.Execute(schedule, new Actor("tech", Role.Technician),
            new ScheduleCommand.SetAvailability(schedule.Revision, primary.Id, false), now);
        if (outage is not Result<ScheduleChange>.Ok(var change))
            return true;

        var replacements = RecoveryPlanner.Build(change.Schedule, primary, now).Moves
            .Where(move => futureSlots[move.SlotId].StationId == stations[DemoCatalog.Backup.Name].Id)
            .Select(move => move.BookingId).ToHashSet();
        return laserBookings.Any(booking => !replacements.Contains(booking.Id));
    }
}
