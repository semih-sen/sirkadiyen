using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCalendarInventoryReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastCalendarInventoryAtUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ManagedCalendarUnavailableAtUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_google_calendar_connections_inventory_due",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            columns: new[] { "Status", "InitialSyncState", "ManagedCalendarUnavailableAtUtc", "LastCalendarInventoryAtUtc" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_google_calendar_connections_unavailable_calendar",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            sql: "\"ManagedCalendarUnavailableAtUtc\" IS NULL OR (\"ManagedCalendarId\" IS NOT NULL AND \"InitialSyncState\" = 'Completed')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_google_calendar_connections_inventory_due",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropCheckConstraint(
            name: "ck_google_calendar_connections_unavailable_calendar",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropColumn(
            name: "LastCalendarInventoryAtUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropColumn(
            name: "ManagedCalendarUnavailableAtUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections");
    }
}
