using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <summary>
/// Replaces the single academic department with every department the source
/// names, adds the curriculum block, and records stale parse-run recovery
/// (ADR-047, ADR-049, ADR-050).
/// </summary>
/// <remarks>
/// The scaffolded version of this migration renamed <c>Department</c> to
/// <c>CurriculumBlock</c>, which would silently reinterpret a department as a
/// curriculum block. They are different facts, so the column is added, any
/// existing department is carried into the new list, and only then is the old
/// column dropped. No row can hold one today, because nothing ever wrote that
/// column, but the drop is written as a data migration rather than relying on
/// that.
/// </remarks>
public partial class AddCanonicalDepartmentsAndParseRunRecovery : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CurriculumBlock",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        // Nullable first, then backfilled, then required. An empty list means
        // "the source named no department", which is a real state and must not
        // be stored as null.
        migrationBuilder.AddColumn<string>(
            name: "Departments",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "jsonb",
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE sirkadiyen.canonical_schedule_records
            SET "Departments" = CASE
                WHEN "Department" IS NULL THEN '[]'::jsonb
                ELSE jsonb_build_array("Department")
            END;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "Departments",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "jsonb",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "jsonb",
            oldNullable: true);

        migrationBuilder.DropColumn(
            name: "Department",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "LastStaleRecoveryAtUtc",
            schema: "sirkadiyen",
            table: "parse_runs",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LastStaleRecoveryAtUtc",
            schema: "sirkadiyen",
            table: "parse_runs");

        migrationBuilder.AddColumn<string>(
            name: "Department",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);

        // Only a lone department fits back into a single column. A record that
        // named several is an integrated session, and picking one of them would
        // be a fabrication, so the reverse migration keeps none.
        migrationBuilder.Sql(
            """
            UPDATE sirkadiyen.canonical_schedule_records
            SET "Department" = "Departments"->>0
            WHERE jsonb_array_length("Departments") = 1;
            """);

        migrationBuilder.DropColumn(
            name: "Departments",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");

        migrationBuilder.DropColumn(
            name: "CurriculumBlock",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");
    }
}
