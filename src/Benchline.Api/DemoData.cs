using System.Collections.Immutable;
using System.Data;
using Benchline.Application;
using Benchline.Contracts;
using Benchline.Domain;
using Benchline.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Benchline.Api;

public static class DemoData
{
    public static async Task Initialize(IServiceProvider services, bool demo, IConfiguration configuration)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkshopDb>();
        await db.Database.MigrateAsync();
        if (!demo)
            return;

        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var revision = await db.Revisions.SingleAsync();
        var stations = await db.Stations.AsNoTracking().ToArrayAsync();
        var slots = await db.Slots.AsNoTracking().ToArrayAsync();
        var bookings = await db.Bookings.AsNoTracking().ToArrayAsync();
        var schedule = new Schedule(revision.Value, stations.ToImmutableArray(), slots.ToImmutableArray(), bookings.ToImmutableArray());
        await UpdateAccounts(db, scope.ServiceProvider, configuration);
        var renew = DemoSchedule.NeedsRenewal(schedule, now);
        var demoStations = UpdateStations(db, stations, renew);
        if (renew)
            await Renew(db, demoStations, revision, now);

        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static async Task UpdateAccounts(WorkshopDb db, IServiceProvider services, IConfiguration configuration)
    {
        var accounts = await db.Accounts.AsNoTracking().ToDictionaryAsync(account => account.Id);
        foreach (var (id, role) in DemoAccounts)
        {
            var name = DemoCatalog.DisplayName(id)!;
            if (accounts.TryGetValue(id, out var existing))
            {
                if (name != existing.Name)
                    db.Accounts.Update(existing with { Name = name });
                continue;
            }

            var password = configuration["Workshop:DemoPassword"]
                ?? throw new InvalidOperationException("Set Workshop:DemoPassword before starting the demo.");
            var hasher = services.GetRequiredService<IPasswordHasher<Account>>();
            var account = new Account(id, id, name, role, "");
            db.Accounts.Add(account with { PasswordHash = hasher.HashPassword(account, password) });
        }
    }

    private static Dictionary<string, Station> UpdateStations(WorkshopDb db, Station[] stations, bool renew)
    {
        var updated = new Dictionary<string, Station>();
        foreach (var demo in DemoCatalog.Stations)
        {
            var existing = stations.SingleOrDefault(station => station.Name == demo.Name);
            var station = existing is null
                ? new Station(Guid.NewGuid(), demo.Name, demo.Capability, demo.Location, StationStatus.Online)
                : existing with { Location = demo.Location, Status = renew ? StationStatus.Online : existing.Status };
            if (existing is null)
                db.Stations.Add(station);
            else if (existing != station)
                db.Stations.Update(station);
            updated.Add(demo.Name, station);
        }
        return updated;
    }

    private static async Task Renew(WorkshopDb db, Dictionary<string, Station> stations, WorkshopRevision revision, DateTimeOffset now)
    {
        var primary = stations[DemoCatalog.Primary.Name];
        var backup = stations[DemoCatalog.Backup.Name];
        var printer = stations[DemoCatalog.Printer.Name];
        var mill = stations[DemoCatalog.Mill.Name];

        Guid[] demoIds = [primary.Id, backup.Id, printer.Id, mill.Id];
        var oldSlots = db.Slots.Where(slot => demoIds.Contains(slot.StationId));
        await db.Bookings.Where(booking => oldSlots.Any(slot => slot.Id == booking.SlotId)).ExecuteDeleteAsync();
        await oldSlots.ExecuteDeleteAsync();

        var start = new DateTimeOffset(now.Year, now.Month, now.Day, now.Hour, 0, 0, TimeSpan.Zero).AddHours(2);
        var primarySlots = Sessions(primary, start, 0, 1, 4, 5);
        var backupSlots = Sessions(backup, start, 1, 2, 4, 5);
        var printerSlots = Sessions(printer, start, 0, 1, 2, 3, 4, 5);
        var millSlots = Sessions(mill, start, 0, 2, 4, 5);
        db.Slots.AddRange(primarySlots.Concat(backupSlots).Concat(printerSlots).Concat(millSlots));

        db.Bookings.AddRange(
            new Booking(Guid.NewGuid(), "member", primarySlots[0].Id, backupSlots[1].End, BookingStatus.Reserved),
            new Booking(Guid.NewGuid(), "member2", primarySlots[1].Id, backupSlots[0].End, BookingStatus.Reserved),
            new Booking(Guid.NewGuid(), "member", printerSlots[2].Id, printerSlots[2].End, BookingStatus.Reserved));

        var firstStart = revision.Value == 0;
        revision.Value++;
        db.AuditEntries.Add(new AuditEntry(revision.Value, now, "system",
            firstStart ? "Workshop opened" : "Demo schedule renewed",
            firstStart ? "4 stations provisioned" : "Practice sessions and reservations reset"));
    }

    private static Slot[] Sessions(Station station, DateTimeOffset start, params int[] offsets) =>
        offsets.Select(offset => new Slot(Guid.NewGuid(), station.Id, start.AddHours(offset))).ToArray();

    private static readonly (string Id, Role Role)[] DemoAccounts =
    [
        ("member", Role.Member), ("member2", Role.Member),
        ("tech", Role.Technician), ("coord", Role.Coordinator)
    ];
}
