using System.Collections.Immutable;

namespace Benchline.Domain;

public static class ScheduleRules
{
    private const int LatestReplacementDays = 7;

    public static readonly Fault Stale = new(FaultKind.Conflict, "stale-revision", "The schedule changed. Refresh and try again.");
    public static readonly Fault Forbidden = new(FaultKind.Forbidden, "forbidden", "Your role cannot perform this action.");

    public static Result<ScheduleChange> Execute(Schedule state, Actor actor, ScheduleCommand command, DateTimeOffset now)
    {
        bool? authorized = command switch
        {
            ScheduleCommand.Reserve or ScheduleCommand.Cancel => actor.Role == Role.Member,
            ScheduleCommand.SetAvailability => actor.Role is Role.Technician or Role.Coordinator,
            ScheduleCommand.Publish => actor.Role == Role.Coordinator,
            _ => null
        };

        if (authorized is null)
            return Unsupported();
        if (!authorized.Value)
            return new Result<ScheduleChange>.Err(Forbidden);
        if (state.Revision != command.Revision)
            return new Result<ScheduleChange>.Err(Stale);

        return command switch
        {
            ScheduleCommand.Reserve reserve => Reserve(state, actor, reserve, now),
            ScheduleCommand.Cancel cancel => Cancel(state, actor, cancel, now),
            ScheduleCommand.SetAvailability availability => SetAvailability(state, availability, now),
            ScheduleCommand.Publish publish => Publish(state, publish, now),
            _ => Unsupported()
        };
    }

    public static Result<RecoveryPlan> Preview(Schedule state, Actor actor, Guid stationId, DateTimeOffset now)
    {
        if (actor.Role != Role.Coordinator)
            return new Result<RecoveryPlan>.Err(Forbidden);

        var station = state.Stations.SingleOrDefault(item => item.Id == stationId);
        if (station is null)
            return new Result<RecoveryPlan>.Err(new Fault(FaultKind.Missing, "station-missing", "Station not found."));

        return new Result<RecoveryPlan>.Ok(RecoveryPlanner.Build(state, station, now));
    }

    private static Result<ScheduleChange> Reserve(Schedule state, Actor actor, ScheduleCommand.Reserve command, DateTimeOffset now)
    {
        var slot = state.Slots.SingleOrDefault(item => item.Id == command.SlotId);
        if (slot is null)
            return Reject(FaultKind.Missing, "slot-missing", "Session not found.");
        if (slot.Start <= now || command.Deadline < slot.End || command.Deadline > slot.End.AddDays(LatestReplacementDays))
            return Reject(FaultKind.Invalid, "invalid-deadline", "Choose a future session and a deadline within seven days of its end.");
        if (state.Bookings.Any(item => item.Id == command.BookingId))
            return Reject(FaultKind.Conflict, "duplicate-booking", "This reservation was already submitted.");
        if (state.Stations.Single(item => item.Id == slot.StationId).Status == StationStatus.OutOfService
            || state.Bookings.Any(item => item.SlotId == slot.Id && item.Status == BookingStatus.Reserved))
            return Reject(FaultKind.Conflict, "slot-unavailable", "The session is no longer available.");

        var booking = new Booking(command.BookingId, actor.Id, slot.Id, command.Deadline, BookingStatus.Reserved);
        return Accept(state with { Bookings = state.Bookings.Add(booking) }, new ScheduleEvent.Reserved(booking.Id));
    }

    private static Result<ScheduleChange> Cancel(Schedule state, Actor actor, ScheduleCommand.Cancel command, DateTimeOffset now)
    {
        var booking = state.Bookings.SingleOrDefault(item => item.Id == command.BookingId && item.OwnerId == actor.Id);
        if (booking is null)
            return Reject(FaultKind.Missing, "booking-missing", "Reservation not found.");
        if (booking.Status == BookingStatus.Cancelled
            || (booking.Status == BookingStatus.Reserved && state.Slots.Single(slot => slot.Id == booking.SlotId).Start <= now)
            || (booking.Status == BookingStatus.Displaced && booking.Deadline <= now))
            return Reject(FaultKind.Conflict, "booking-closed", "This reservation can no longer be cancelled.");

        return Accept(state with { Bookings = state.Bookings.Replace(booking, booking with { Status = BookingStatus.Cancelled }) },
            new ScheduleEvent.Cancelled(booking.Id));
    }

    private static Result<ScheduleChange> SetAvailability(Schedule state, ScheduleCommand.SetAvailability command, DateTimeOffset now)
    {
        var station = state.Stations.SingleOrDefault(item => item.Id == command.StationId);
        if (station is null)
            return Reject(FaultKind.Missing, "station-missing", "Station not found.");

        var nextStatus = command.Online ? StationStatus.Online : StationStatus.OutOfService;
        if (station.Status == nextStatus)
            return Reject(FaultKind.Conflict, "same-status", "The station is already in that state.");

        var future = state.Slots.Where(slot => slot.StationId == station.Id && slot.Start > now)
            .Select(slot => slot.Id).ToHashSet();
        var disrupted = command.Online ? [] : state.Bookings
            .Where(booking => booking.Status == BookingStatus.Reserved && future.Contains(booking.SlotId)).ToArray();
        var ids = disrupted.Select(booking => booking.Id).ToHashSet();
        var next = state with
        {
            Stations = state.Stations.Replace(station, station with { Status = nextStatus }),
            Bookings = state.Bookings.Select(booking => ids.Contains(booking.Id)
                ? booking with { Status = BookingStatus.Displaced } : booking).ToImmutableArray()
        };
        return Accept(next, new ScheduleEvent.AvailabilityChanged(station.Id, command.Online, disrupted.Length));
    }

    private static Result<ScheduleChange> Publish(Schedule state, ScheduleCommand.Publish command, DateTimeOffset now)
    {
        var station = state.Stations.SingleOrDefault(item => item.Id == command.StationId);
        if (station is null)
            return Reject(FaultKind.Missing, "station-missing", "Station not found.");

        var plan = RecoveryPlanner.Build(state, station, now);
        if (plan.Moves.IsEmpty)
            return Reject(FaultKind.Conflict, "no-replacements", "There are no compatible sessions to publish.");
        if (command.Accepted.IsDefault || !plan.Moves.SequenceEqual(command.Accepted))
            return Reject(FaultKind.Conflict, "changed-plan", "The replacement sessions changed. Preview the plan again.");

        var moves = plan.Moves.ToDictionary(move => move.BookingId);
        var next = state with
        {
            Bookings = state.Bookings.Select(booking => moves.TryGetValue(booking.Id, out var move)
                ? booking with { SlotId = move.SlotId, Status = BookingStatus.Reserved } : booking).ToImmutableArray()
        };
        return Accept(next, new ScheduleEvent.Recovered(station.Id, moves.Count));
    }

    private static Result<ScheduleChange> Unsupported() =>
        Reject(FaultKind.Invalid, "unsupported-command", "The schedule command is not supported.");

    private static Result<ScheduleChange> Accept(Schedule state, ScheduleEvent @event) =>
        new Result<ScheduleChange>.Ok(new ScheduleChange(state with { Revision = state.Revision + 1 }, @event));

    private static Result<ScheduleChange> Reject(FaultKind kind, string code, string message) =>
        new Result<ScheduleChange>.Err(new Fault(kind, code, message));
}
