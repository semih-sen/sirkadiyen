using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSnapshotPayloadRetention : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_source_snapshots_ScheduleSourceId",
            schema: "sirkadiyen",
            table: "source_snapshots");

        migrationBuilder.AlterColumn<string>(
            name: "Payload",
            schema: "sirkadiyen",
            table: "source_snapshots",
            type: "jsonb",
            nullable: true,
            oldClrType: typeof(string),
            oldType: "jsonb");

        migrationBuilder.AddColumn<string>(
            name: "AcademicYear",
            schema: "sirkadiyen",
            table: "source_snapshots",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        // Historical snapshots predate the denormalized year. Their source
        // row still carries the year under which they were acquired in the
        // current pre-production database, so copy it before making the
        // column required. Future inserts set it directly.
        migrationBuilder.Sql(
            """
            UPDATE sirkadiyen.source_snapshots AS snapshot
            SET "AcademicYear" = source."AcademicYear"
            FROM sirkadiyen.schedule_sources AS source
            WHERE source."Id" = snapshot."ScheduleSourceId";
            """);

        migrationBuilder.AlterColumn<string>(
            name: "AcademicYear",
            schema: "sirkadiyen",
            table: "source_snapshots",
            type: "character varying(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(20)",
            oldMaxLength: 20,
            oldNullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "PayloadPrunedAtUtc",
            schema: "sirkadiyen",
            table: "source_snapshots",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_source_snapshots_ScheduleSourceId_AcademicYear_AcquiredAtUtc",
            schema: "sirkadiyen",
            table: "source_snapshots",
            columns: new[] { "ScheduleSourceId", "AcademicYear", "AcquiredAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_source_snapshots_ScheduleSourceId_AcademicYear_AcquiredAtUtc",
            schema: "sirkadiyen",
            table: "source_snapshots");

        migrationBuilder.DropColumn(
            name: "AcademicYear",
            schema: "sirkadiyen",
            table: "source_snapshots");

        migrationBuilder.DropColumn(
            name: "PayloadPrunedAtUtc",
            schema: "sirkadiyen",
            table: "source_snapshots");

        migrationBuilder.AlterColumn<string>(
            name: "Payload",
            schema: "sirkadiyen",
            table: "source_snapshots",
            type: "jsonb",
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "jsonb",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_source_snapshots_ScheduleSourceId",
            schema: "sirkadiyen",
            table: "source_snapshots",
            column: "ScheduleSourceId");
    }
}
