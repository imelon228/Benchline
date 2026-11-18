namespace Benchline.Contracts;

public sealed record SessionDto(string Name, string Role);
public sealed record TokenDto(string Token);
public sealed record SignInRequest(string UserName, string Password);
public sealed record StationDto(Guid Id, string Name, string Capability, string Location, bool Online, int Displaced);
public sealed record SlotDto(Guid Id, Guid StationId, DateTimeOffset Start, DateTimeOffset End, bool Available);
public sealed record BookingDto(Guid Id, Guid SlotId, string Status, DateTimeOffset Deadline);
public sealed record BoardDto(long Revision, DateTimeOffset AsOf, StationDto[] Stations, SlotDto[] Slots, BookingDto[] Bookings);
/// <summary>Place a reservation against the schedule revision the member reviewed.</summary>
/// <param name="Revision">The current revision from the workshop board.</param>
/// <param name="BookingId">A caller-created identifier for this reservation.</param>
/// <param name="SlotId">An available equipment session on the board.</param>
/// <param name="Deadline">The latest acceptable finish for a replacement, expressed as an ISO 8601 timestamp.</param>
public sealed record ReserveRequest(long Revision, Guid BookingId, Guid SlotId, DateTimeOffset Deadline);
public sealed record RevisionRequest(long Revision);
public sealed record AvailabilityRequest(long Revision, bool Online);
public sealed record MoveDto(Guid BookingId, Guid SlotId);
public sealed record PlanDto(long Revision, MoveDto[] Moves, int Unmatched);
/// <summary>Publish the replacement assignments reviewed by the coordinator.</summary>
/// <param name="Revision">The revision included in the preview.</param>
/// <param name="Moves">The ordered list of replacement assignments included in the preview.</param>
public sealed record PublishRequest(long Revision, MoveDto[] Moves);
public sealed record RevisionDto(long Revision);
public sealed record AuditDto(long Revision, DateTimeOffset RecordedAt, string ActorId, string Event, string Details);

public static class AuditQuery
{
    public const int DefaultPageSize = 25;
}

public static class DemoSignIn
{
    public const string Header = "X-Benchline-Demo";
    public const string DefaultPassword = "Benchline-Demo!26";
}
