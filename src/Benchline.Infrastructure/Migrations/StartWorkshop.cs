using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Benchline.Infrastructure.Migrations;

[DbContext(typeof(WorkshopDb))]
[Migration("20261115090000_StartWorkshop")]
public sealed class StartWorkshop : Migration
{
    protected override void Up(MigrationBuilder migration)
    {
        migration.CreateTable("Revisions", table => new
        {
            Id = table.Column<int>(nullable: false),
            Value = table.Column<long>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Revisions", row => row.Id));

        migration.CreateTable("Accounts", table => new
        {
            Id = table.Column<string>(maxLength: 80, nullable: false),
            UserName = table.Column<string>(maxLength: 80, nullable: false),
            Name = table.Column<string>(nullable: false),
            Role = table.Column<int>(nullable: false),
            PasswordHash = table.Column<string>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Accounts", row => row.Id));

        migration.CreateTable("Stations", table => new
        {
            Id = table.Column<Guid>(nullable: false),
            Name = table.Column<string>(nullable: false),
            Capability = table.Column<string>(nullable: false),
            Location = table.Column<string>(nullable: false),
            Status = table.Column<int>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_Stations", row => row.Id));

        migration.CreateTable("Slots", table => new
        {
            Id = table.Column<Guid>(nullable: false),
            StationId = table.Column<Guid>(nullable: false),
            Start = table.Column<long>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Slots", row => row.Id);
            table.ForeignKey("FK_Slots_Stations_StationId", row => row.StationId, "Stations", "Id", onDelete: ReferentialAction.Cascade);
        });

        migration.CreateTable("Bookings", table => new
        {
            Id = table.Column<Guid>(nullable: false),
            OwnerId = table.Column<string>(maxLength: 80, nullable: false),
            SlotId = table.Column<Guid>(nullable: false),
            Deadline = table.Column<long>(nullable: false),
            Status = table.Column<int>(nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_Bookings", row => row.Id);
            table.ForeignKey("FK_Bookings_Slots_SlotId", row => row.SlotId, "Slots", "Id", onDelete: ReferentialAction.Cascade);
            table.ForeignKey("FK_Bookings_Accounts_OwnerId", row => row.OwnerId, "Accounts", "Id", onDelete: ReferentialAction.Cascade);
        });

        migration.CreateTable("AuditEntries", table => new
        {
            Revision = table.Column<long>(nullable: false),
            RecordedAt = table.Column<long>(nullable: false),
            ActorId = table.Column<string>(nullable: false),
            Event = table.Column<string>(nullable: false),
            Details = table.Column<string>(nullable: false)
        }, constraints: table => table.PrimaryKey("PK_AuditEntries", row => row.Revision));

        migration.CreateIndex("IX_Accounts_UserName", "Accounts", "UserName", unique: true);
        migration.CreateIndex("IX_Slots_StationId_Start", "Slots", new[] { "StationId", "Start" }, unique: true);
        migration.CreateIndex("IX_Bookings_OwnerId_Status", "Bookings", new[] { "OwnerId", "Status" });
        migration.CreateIndex("IX_Bookings_SlotId", "Bookings", "SlotId", unique: true, filter: "\"Status\" = 0");
        migration.Sql("INSERT INTO \"Revisions\" (\"Id\", \"Value\") VALUES (1, 0);");
    }

    protected override void Down(MigrationBuilder migration)
    {
        migration.DropTable("AuditEntries");
        migration.DropTable("Bookings");
        migration.DropTable("Accounts");
        migration.DropTable("Slots");
        migration.DropTable("Stations");
        migration.DropTable("Revisions");
    }
}
