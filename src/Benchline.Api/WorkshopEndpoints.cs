using System.Collections.Immutable;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using Benchline.Application;
using Benchline.Contracts;
using Benchline.Domain;
using Microsoft.AspNetCore.SignalR;

namespace Benchline.Api;

public sealed class WorkshopHub : Hub;

internal static class WorkshopEndpoints
{
    public static void MapWorkshop(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapSession();
        var authenticated = api.MapGroup("").RequireAuthorization();
        var writes = authenticated.MapGroup("").AddEndpointFilter<RequestTokenFilter>();
        authenticated.MapGet("/board", Board).WithSummary("Read the workshop board for the signed-in member.");
        writes.MapPost("/bookings", (ReserveRequest request, Mutation mutation) => mutation.Execute(
            new ScheduleCommand.Reserve(request.Revision, request.BookingId, request.SlotId, request.Deadline)))
            .RequireAuthorization(policy => policy.RequireRole(nameof(Role.Member)))
            .WithSummary("Reserve an open session through a specific schedule revision.");
        writes.MapPost("/bookings/{id:guid}/cancel", (Guid id, RevisionRequest request, Mutation mutation) =>
            mutation.Execute(new ScheduleCommand.Cancel(request.Revision, id)))
            .RequireAuthorization(policy => policy.RequireRole(nameof(Role.Member)));
        writes.MapPut("/stations/{id:guid}", (Guid id, AvailabilityRequest request, Mutation mutation) =>
            mutation.Execute(new ScheduleCommand.SetAvailability(request.Revision, id, request.Online)))
            .RequireAuthorization(policy => policy.RequireRole(nameof(Role.Technician), nameof(Role.Coordinator)));

        var coordinator = authenticated.MapGroup("").RequireAuthorization(policy => policy.RequireRole(nameof(Role.Coordinator)));
        coordinator.MapGet("/recovery/{id:guid}", Preview).WithSummary("Preview compatible replacements for displaced reservations.");
        coordinator.MapPost("/recovery/{id:guid}", (Guid id, PublishRequest request, Mutation mutation) =>
        {
            if (request.Moves is null || request.Moves.Length is 0 or > 500 || request.Moves.Any(move => move is null))
                return Task.FromResult(Results.Problem("Supply the moves from the current preview.", statusCode: 400));

            return mutation.Execute(new ScheduleCommand.Publish(request.Revision, id,
                request.Moves.Select(move => new Assignment(move.BookingId, move.SlotId)).ToImmutableArray()));
        }).AddEndpointFilter<RequestTokenFilter>().WithSummary("Apply the exact preview in one transaction.");
        coordinator.MapGet("/audit", Audit);
        coordinator.MapGet("/audit.csv", ExportAudit);
    }

    private static async Task<BoardDto> Board(ClaimsPrincipal principal, Workshop workshop,
        TimeProvider time, CancellationToken cancellationToken)
    {
        var actor = SessionEndpoints.Actor(principal);
        var state = await workshop.Read(cancellationToken);
        var now = time.GetUtcNow();
        var statuses = state.Stations.ToDictionary(station => station.Id, station => station.Status);
        var occupied = state.Bookings.Where(booking => booking.Status == BookingStatus.Reserved)
            .Select(booking => booking.SlotId).ToHashSet();
        var slotStations = state.Slots.ToDictionary(slot => slot.Id, slot => slot.StationId);
        var displaced = state.Bookings.Where(item => item.Status == BookingStatus.Displaced && item.Deadline > now)
            .ToLookup(item => slotStations[item.SlotId]);

        return new BoardDto(state.Revision, now,
            state.Stations.Select(station => new StationDto(station.Id, station.Name, station.Capability,
                station.Location, station.Status == StationStatus.Online,
                actor.Role == Role.Member ? 0 : displaced[station.Id].Count())).ToArray(),
            state.Slots.Select(slot => new SlotDto(slot.Id, slot.StationId, slot.Start, slot.End,
                slot.Start > now && statuses[slot.StationId] == StationStatus.Online && !occupied.Contains(slot.Id))).ToArray(),
            state.Bookings.Where(booking => booking.OwnerId == actor.Id)
                .Select(booking => new BookingDto(booking.Id, booking.SlotId, booking.Status.ToString(), booking.Deadline)).ToArray());
    }

    private static async Task<IResult> Preview(Guid id, ClaimsPrincipal principal, Workshop workshop, CancellationToken cancellationToken)
    {
        var result = await workshop.Preview(SessionEndpoints.Actor(principal), id, cancellationToken);
        return result switch
        {
            Result<RecoveryPlan>.Ok(var plan) => Results.Ok(new PlanDto(plan.Revision,
                plan.Moves.Select(move => new MoveDto(move.BookingId, move.SlotId)).ToArray(), plan.Unmatched.Length)),
            Result<RecoveryPlan>.Err(var fault) => Problem(fault),
            _ => throw new InvalidOperationException("Unknown preview result.")
        };
    }

    private static async Task<AuditDto[]> Audit(long? before, int? limit, Workshop workshop, CancellationToken cancellationToken)
    {
        var rows = await workshop.Audit(before ?? long.MaxValue, limit ?? AuditQuery.DefaultPageSize, cancellationToken);
        return rows.Select(row => new AuditDto(row.Revision, row.RecordedAt, row.ActorId, row.Event, row.Details)).ToArray();
    }

    private static async Task<IResult> ExportAudit(Workshop workshop, CancellationToken cancellationToken)
    {
        var rows = await Audit(null, Workshop.MaximumAuditPageSize, workshop, cancellationToken);
        var csv = new StringBuilder("Revision,Recorded UTC,Account,Event,Details,Recorded Astana time\r\n");
        foreach (var row in rows)
        {
            csv.Append(row.Revision.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(Cell(row.RecordedAt.ToString("O", CultureInfo.InvariantCulture))).Append(',')
                .Append(Cell(row.ActorId)).Append(',').Append(Cell(row.Event)).Append(',').Append(Cell(row.Details)).Append(',')
                .Append(Cell(AstanaTime.Format(row.RecordedAt, "O"))).Append("\r\n");
        }
        return Results.File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv; charset=utf-8", "benchline-audit.csv");
    }

    private static string Cell(string value)
    {
        var safe = value.TrimStart() is ['=' or '+' or '-' or '@', ..] ? "'" + value : value;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    internal static IResult Problem(Fault fault) => Results.Problem(
        detail: fault.Message,
        statusCode: fault.Kind switch
        {
            FaultKind.Invalid => 400,
            FaultKind.Missing => 404,
            FaultKind.Conflict => 409,
            FaultKind.Forbidden => 403,
            _ => 500
        },
        extensions: new Dictionary<string, object?> { ["code"] = fault.Code });
}

internal sealed class Mutation(Workshop workshop, IHttpContextAccessor accessor,
    IHubContext<WorkshopHub> hub, ILogger<Mutation> logger)
{
    public async Task<IResult> Execute(ScheduleCommand command)
    {
        var context = accessor.HttpContext!;
        var result = await workshop.Execute(SessionEndpoints.Actor(context.User), command, context.RequestAborted);
        if (result is Result<long>.Err(var fault))
            return WorkshopEndpoints.Problem(fault);

        var revision = ((Result<long>.Ok)result).Value;
        try
        {
            await hub.Clients.All.SendAsync("ScheduleChanged", revision);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "A schedule update could not be broadcast for revision {Revision}", revision);
        }
        return Results.Ok(new RevisionDto(revision));
    }
}
