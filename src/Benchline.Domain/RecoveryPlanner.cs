using System.Collections.Immutable;

namespace Benchline.Domain;

public static class RecoveryPlanner
{
    public static RecoveryPlan Build(Schedule schedule, Station source, DateTimeOffset now)
    {
        var sourceSlots = schedule.Slots.Where(slot => slot.StationId == source.Id)
            .ToDictionary(slot => slot.Id);
        var backupStations = schedule.Stations
            .Where(station => station.Id != source.Id && station.Status == StationStatus.Online
                && station.Capability == source.Capability)
            .Select(station => station.Id).ToHashSet();
        var occupied = schedule.Bookings
            .Where(booking => booking.Status == BookingStatus.Reserved)
            .Select(booking => booking.SlotId).ToHashSet();

        var available = schedule.Slots
            .Where(slot => slot.Start > now && backupStations.Contains(slot.StationId) && !occupied.Contains(slot.Id))
            .OrderBy(slot => slot.Start).ThenBy(slot => slot.Id).ToArray();
        var displaced = schedule.Bookings
            .Where(booking => booking.Status == BookingStatus.Displaced && booking.Deadline > now
                && sourceSlots.ContainsKey(booking.SlotId))
            .OrderBy(booking => sourceSlots[booking.SlotId].Start).ThenBy(booking => booking.Deadline)
            .ThenBy(booking => booking.Id).ToArray();

        var options = displaced.ToDictionary(booking => booking.Id, booking => available
            .Where(slot => slot.Start >= sourceSlots[booking.SlotId].Start && slot.End <= booking.Deadline)
            .Select(slot => slot.Id).ToArray());

        var slotOwners = new Dictionary<Guid, Guid>();
        foreach (var booking in displaced)
        {
            TryAssign(booking.Id, options, slotOwners, []);
        }

        var assignments = slotOwners.ToDictionary(entry => entry.Value, entry => entry.Key);
        var moves = displaced.Where(booking => assignments.ContainsKey(booking.Id))
            .Select(booking => new Assignment(booking.Id, assignments[booking.Id])).ToImmutableArray();
        var unmatched = displaced.Where(booking => !assignments.ContainsKey(booking.Id))
            .Select(booking => booking.Id).ToImmutableArray();

        return new RecoveryPlan(schedule.Revision, moves, unmatched);
    }

    private static bool TryAssign(Guid bookingId, Dictionary<Guid, Guid[]> options,
        Dictionary<Guid, Guid> slotOwners, HashSet<Guid> visited)
    {
        foreach (var slotId in options[bookingId])
        {
            if (!visited.Add(slotId))
                continue;

            if (!slotOwners.TryGetValue(slotId, out var currentOwner)
                || TryAssign(currentOwner, options, slotOwners, visited))
            {
                slotOwners[slotId] = bookingId;
                return true;
            }
        }

        return false;
    }
}
