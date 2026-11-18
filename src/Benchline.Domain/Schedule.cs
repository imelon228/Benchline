using System.Collections.Immutable;

namespace Benchline.Domain;

public enum Role { Member, Technician, Coordinator }
public enum StationStatus { Online, OutOfService }
public enum BookingStatus { Reserved, Displaced, Cancelled }
public enum FaultKind { Invalid, Missing, Conflict, Forbidden }

public sealed record Fault(FaultKind Kind, string Code, string Message);

public abstract record Result<T>
{
    private Result() { }

    public sealed record Ok(T Value) : Result<T>;
    public sealed record Err(Fault Fault) : Result<T>;

    public Result<TNext> Map<TNext>(Func<T, TNext> map) => this switch
    {
        Ok(var value) => new Result<TNext>.Ok(map(value)),
        Err(var fault) => new Result<TNext>.Err(fault),
        _ => throw new InvalidOperationException("Unknown result.")
    };
}

public sealed record Actor(string Id, Role Role);
public sealed record Station(Guid Id, string Name, string Capability, string Location, StationStatus Status);
public sealed record Slot(Guid Id, Guid StationId, DateTimeOffset Start)
{
    public DateTimeOffset End => Start.AddHours(1);
}

public sealed record Booking(Guid Id, string OwnerId, Guid SlotId, DateTimeOffset Deadline, BookingStatus Status);

public sealed record Schedule(
    long Revision,
    ImmutableArray<Station> Stations,
    ImmutableArray<Slot> Slots,
    ImmutableArray<Booking> Bookings);

public abstract record ScheduleCommand(long Revision)
{
    public sealed record Reserve(long Expected, Guid BookingId, Guid SlotId, DateTimeOffset Deadline)
        : ScheduleCommand(Expected);

    public sealed record Cancel(long Expected, Guid BookingId) : ScheduleCommand(Expected);

    public sealed record SetAvailability(long Expected, Guid StationId, bool Online) : ScheduleCommand(Expected);

    public sealed record Publish(long Expected, Guid StationId, ImmutableArray<Assignment> Accepted)
        : ScheduleCommand(Expected);
}

public abstract record ScheduleEvent
{
    public sealed record Reserved(Guid BookingId) : ScheduleEvent;
    public sealed record Cancelled(Guid BookingId) : ScheduleEvent;
    public sealed record AvailabilityChanged(Guid StationId, bool Online, int Displaced) : ScheduleEvent;
    public sealed record Recovered(Guid StationId, int Moved) : ScheduleEvent;
}

public sealed record ScheduleChange(Schedule Schedule, ScheduleEvent Event);
public sealed record Assignment(Guid BookingId, Guid SlotId);
public sealed record RecoveryPlan(long Revision, ImmutableArray<Assignment> Moves, ImmutableArray<Guid> Unmatched);
