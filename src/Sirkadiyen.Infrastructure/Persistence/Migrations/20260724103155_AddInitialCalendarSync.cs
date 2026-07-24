using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddInitialCalendarSync : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Defaults to the initial state rather than EF's empty string: an empty value would
        // violate the check constraint added below, and any connection that predates this
        // column has not begun its initial sync.
        migrationBuilder.AddColumn<string>(
            name: "InitialSyncState",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Pending");

        migrationBuilder.CreateTable(
            name: "user_calendar_event_mappings",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                StableIdentity = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CanonicalRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                GoogleCalendarId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                GoogleEventId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                ContentHash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_calendar_event_mappings", x => x.Id);
                table.ForeignKey(
                    name: "FK_user_calendar_event_mappings_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_google_calendar_connections_initial_sync_state",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            sql: "\"InitialSyncState\" IN ('Pending', 'InProgress', 'Completed')");

        migrationBuilder.CreateIndex(
            name: "IX_user_calendar_event_mappings_UserId_StableIdentity",
            schema: "sirkadiyen",
            table: "user_calendar_event_mappings",
            columns: new[] { "UserId", "StableIdentity" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "user_calendar_event_mappings",
            schema: "sirkadiyen");

        migrationBuilder.DropCheckConstraint(
            name: "ck_google_calendar_connections_initial_sync_state",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropColumn(
            name: "InitialSyncState",
            schema: "sirkadiyen",
            table: "google_calendar_connections");
    }
}
