using System.Collections.Immutable;
using System.Data;
using Benchline.Application;
using Benchline.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Benchline.Infrastructure;

public sealed class EfScheduleRepository(WorkshopDb db) : IScheduleRepository
{
    public async Task<Schedule> Read(CancellationToken cancellationToken)
    {
        var isolation = db.Database.IsSqlite() ? IsolationLevel.Serializable : IsolationLevel.RepeatableRead;
        await using var transaction = await db.Database.BeginTransactionAsync(isolation, cancellationToken);
        var revision = await db.Revisions.AsNoTracking().SingleAsync(cancellationToken);
        var stations = await db.Stations.AsNoTracking().OrderBy(item => item.Name).ToArrayAsync(cancellationToken);
        var slots = await db.Slots.AsNoTracking().OrderBy(item => item.Start).ThenBy(item => item.Id).ToArrayAsync(cancellationToken);
        var bookings = await db.Bookings.AsNoTracking().Where(item => item.Status != BookingStatus.Cancelled)
            .ToArrayAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new Schedule(revision.Value, stations.ToImmutableArray(), slots.ToImmutableArray(), bookings.ToImmutableArray());
    }

    public async Task<bool> TryWrite(Schedule before, Schedule after, AuditEntry audit, CancellationToken cancellationToken)
    {
        var revision = new WorkshopRevision { Id = 1, Value = after.Revision };
        db.Attach(revision).Property(item => item.Value).OriginalValue = before.Revision;

        var oldStations = before.Stations.ToDictionary(item => item.Id);
        db.Stations.UpdateRange(after.Stations.Where(station => station != oldStations[station.Id]));

        var oldBookings = before.Bookings.ToDictionary(item => item.Id);
        foreach (var booking in after.Bookings)
        {
            if (!oldBookings.TryGetValue(booking.Id, out var original))
                db.Bookings.Add(booking);
            else if (booking != original)
                db.Bookings.Update(booking);
        }

        db.AuditEntries.Add(audit);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 5 or 19 }
            or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.SerializationFailure })
        {
            return false;
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> Audit(long beforeRevision, int limit, CancellationToken cancellationToken) =>
        await db.AuditEntries.AsNoTracking().Where(item => item.Revision < beforeRevision)
            .OrderByDescending(item => item.Revision).Take(limit).ToArrayAsync(cancellationToken);
}
