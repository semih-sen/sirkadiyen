using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCalendarReconciliationCursor : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ReconciliationCursorDiffId",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ReconciliationCursorDispatchedAtUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ReconciliationRequiredSinceUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_google_calendar_connections_reconciliation_pending",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            columns: new[] { "Status", "InitialSyncState", "ReconciliationRequiredSinceUtc" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_google_calendar_connections_reconciliation_cursor",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            sql: "(\"ReconciliationRequiredSinceUtc\" IS NULL AND \"ReconciliationCursorDispatchedAtUtc\" IS NULL AND \"ReconciliationCursorDiffId\" IS NULL) OR (\"ReconciliationRequiredSinceUtc\" IS NOT NULL AND \"ReconciliationCursorDispatchedAtUtc\" IS NOT NULL AND \"ReconciliationCursorDiffId\" IS NOT NULL AND \"InitialSyncState\" = 'Completed' AND \"ManagedCalendarId\" IS NOT NULL AND \"ReconciliationCursorDispatchedAtUtc\" >= \"ReconciliationRequiredSinceUtc\")");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_google_calendar_connections_reconciliation_pending",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropCheckConstraint(
            name: "ck_google_calendar_connections_reconciliation_cursor",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropColumn(
            name: "ReconciliationCursorDiffId",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropColumn(
            name: "ReconciliationCursorDispatchedAtUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropColumn(
            name: "ReconciliationRequiredSinceUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections");
    }
}
