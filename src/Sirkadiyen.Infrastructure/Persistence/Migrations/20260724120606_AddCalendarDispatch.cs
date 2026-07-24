using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCalendarDispatch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Defaults to the initial state rather than EF's empty string: an empty value would
        // violate the check constraint added below, and any diff that predates this column has
        // not been dispatched to calendars yet — pending is exactly right for it (ADR-059).
        migrationBuilder.AddColumn<string>(
            name: "CalendarDispatchState",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            defaultValue: "Pending");

        migrationBuilder.AddColumn<int>(
            name: "DispatchAttempts",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<string>(
            name: "DispatchFailureReason",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "DispatchedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "NextAttemptAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diffs_CalendarDispatchState_NextAttemptAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            columns: new[] { "CalendarDispatchState", "NextAttemptAtUtc" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_schedule_diffs_calendar_dispatch_state",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            sql: "\"CalendarDispatchState\" IN ('Pending', 'Dispatched', 'Failed')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_schedule_diffs_CalendarDispatchState_NextAttemptAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropCheckConstraint(
            name: "ck_schedule_diffs_calendar_dispatch_state",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "CalendarDispatchState",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "DispatchAttempts",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "DispatchFailureReason",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "DispatchedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "NextAttemptAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs");
    }
}
