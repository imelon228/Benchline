using Benchline.Application;
using Benchline.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Benchline.Infrastructure;

public sealed record Account(string Id, string UserName, string Name, Role Role, string PasswordHash);

public sealed class WorkshopRevision
{
    public int Id { get; set; }
    public long Value { get; set; }
}

public sealed class WorkshopDb(DbContextOptions<WorkshopDb> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<WorkshopRevision> Revisions => Set<WorkshopRevision>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToUtcTicks>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<WorkshopRevision>().Property(item => item.Value).IsConcurrencyToken();

        builder.Entity<Account>().HasIndex(item => item.UserName).IsUnique();
        builder.Entity<Account>().Property(item => item.Id).HasMaxLength(80);
        builder.Entity<Account>().Property(item => item.UserName).HasMaxLength(80);

        builder.Entity<Slot>().Ignore(item => item.End);
        builder.Entity<Slot>().HasIndex(item => new { item.StationId, item.Start }).IsUnique();
        builder.Entity<Slot>().HasOne<Station>().WithMany().HasForeignKey(item => item.StationId);

        builder.Entity<Booking>().HasIndex(item => item.SlotId).IsUnique().HasFilter("\"Status\" = 0");
        builder.Entity<Booking>().HasIndex(item => new { item.OwnerId, item.Status });
        builder.Entity<Booking>().HasOne<Slot>().WithMany().HasForeignKey(item => item.SlotId);
        builder.Entity<Booking>().HasOne<Account>().WithMany().HasForeignKey(item => item.OwnerId);

        builder.Entity<AuditEntry>().HasKey(item => item.Revision);
        builder.Entity<AuditEntry>().Property(item => item.Revision).ValueGeneratedNever();
    }

    private sealed class DateTimeOffsetToUtcTicks() : ValueConverter<DateTimeOffset, long>(
        date => date.UtcTicks, ticks => new DateTimeOffset(ticks, TimeSpan.Zero));
}
