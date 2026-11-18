using Benchline.Domain;

namespace Benchline.Application;

public sealed record AuditEntry(long Revision, DateTimeOffset RecordedAt, string ActorId, string Event, string Details);

public interface IScheduleRepository
{
    Task<Schedule> Read(CancellationToken cancellationToken);

    Task<bool> TryWrite(Schedule before, Schedule after, AuditEntry audit, CancellationToken cancellationToken);

    Task<IReadOnlyList<AuditEntry>> Audit(long beforeRevision, int limit, CancellationToken cancellationToken);
}

public sealed class Workshop(IScheduleRepository repository, TimeProvider clock)
{
    public const int MaximumAuditPageSize = 100;

    public Task<Schedule> Read(CancellationToken cancellationToken) => repository.Read(cancellationToken);

    public async Task<Result<RecoveryPlan>> Preview(Actor actor, Guid stationId, CancellationToken cancellationToken) =>
        ScheduleRules.Preview(await repository.Read(cancellationToken), actor, stationId, clock.GetUtcNow());

    public async Task<Result<long>> Execute(Actor actor, ScheduleCommand command, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var before = await repository.Read(cancellationToken);
        var transition = ScheduleRules.Execute(before, actor, command, now);
        if (transition is Result<ScheduleChange>.Err(var fault))
            return new Result<long>.Err(fault);

        var change = ((Result<ScheduleChange>.Ok)transition).Value;
        var (name, details) = change.Event switch
        {
            ScheduleEvent.Reserved(var id) => ("Reserved", id.ToString()),
            ScheduleEvent.Cancelled(var id) => ("Cancelled", id.ToString()),
            ScheduleEvent.AvailabilityChanged(var id, var online, var count) =>
                (online ? "Station restored" : "Station out of service", $"{id} | {count} displaced"),
            ScheduleEvent.Recovered(var id, var count) => ("Recovery published", $"{id} | {count} moved"),
            _ => throw new InvalidOperationException("Unknown schedule event.")
        };
        var audit = new AuditEntry(change.Schedule.Revision, now, actor.Id, name, details);
        var written = await repository.TryWrite(before, change.Schedule, audit, cancellationToken);
        return written ? new Result<long>.Ok(change.Schedule.Revision) : new Result<long>.Err(ScheduleRules.Stale);
    }

    public Task<IReadOnlyList<AuditEntry>> Audit(long beforeRevision, int limit, CancellationToken cancellationToken) =>
        repository.Audit(beforeRevision, Math.Clamp(limit, 1, MaximumAuditPageSize), cancellationToken);
}
