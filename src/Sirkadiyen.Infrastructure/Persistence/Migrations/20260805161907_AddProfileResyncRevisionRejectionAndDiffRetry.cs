using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddProfileResyncRevisionRejectionAndDiffRetry : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RejectedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RejectedBy",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RejectionReason",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "DispatchRetryCount",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastDispatchRetriedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastDispatchRetriedBy",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastDispatchRetryReason",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ProfileResyncRequiredSinceUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_schedule_diffs_dispatch_retry_count",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            sql: "\"DispatchRetryCount\" >= 0");

        migrationBuilder.CreateIndex(
            name: "ix_google_calendar_connections_profile_resync_pending",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            columns: new[] { "Status", "InitialSyncState", "ProfileResyncRequiredSinceUtc" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_google_calendar_connections_profile_resync",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            sql: "\"ProfileResyncRequiredSinceUtc\" IS NULL OR (\"ManagedCalendarId\" IS NOT NULL AND \"InitialSyncState\" = 'Completed')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_schedule_diffs_dispatch_retry_count",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropIndex(
            name: "ix_google_calendar_connections_profile_resync_pending",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropCheckConstraint(
            name: "ck_google_calendar_connections_profile_resync",
            schema: "sirkadiyen",
            table: "google_calendar_connections");

        migrationBuilder.DropColumn(
            name: "RejectedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_revisions");

        migrationBuilder.DropColumn(
            name: "RejectedBy",
            schema: "sirkadiyen",
            table: "schedule_revisions");

        migrationBuilder.DropColumn(
            name: "RejectionReason",
            schema: "sirkadiyen",
            table: "schedule_revisions");

        migrationBuilder.DropColumn(
            name: "DispatchRetryCount",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "LastDispatchRetriedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "LastDispatchRetriedBy",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "LastDispatchRetryReason",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "ProfileResyncRequiredSinceUtc",
            schema: "sirkadiyen",
            table: "google_calendar_connections");
    }
}
