using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class PreserveParserCandidateStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AttemptCount",
            schema: "sirkadiyen",
            table: "parse_runs",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "CandidateId",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "RecordStatus",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            defaultValue: "Scheduled");

        migrationBuilder.Sql(
            """
            UPDATE sirkadiyen.canonical_schedule_records
            SET "CandidateId" = 'legacy-' || "Id"::text
            WHERE "CandidateId" IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "CandidateId",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200,
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_parse_runs_attempt_count",
            schema: "sirkadiyen",
            table: "parse_runs",
            sql: "\"AttemptCount\" > 0");

        migrationBuilder.CreateIndex(
            name: "IX_canonical_schedule_records_ScheduleRevisionId_CandidateId",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            columns: new[] { "ScheduleRevisionId", "CandidateId" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_parse_runs_attempt_count",
            schema: "sirkadiyen",
            table: "parse_runs");

        migrationBuilder.DropIndex(
            name: "IX_canonical_schedule_records_ScheduleRevisionId_CandidateId",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");

        migrationBuilder.DropColumn(
            name: "AttemptCount",
            schema: "sirkadiyen",
            table: "parse_runs");

        migrationBuilder.DropColumn(
            name: "CandidateId",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");

        migrationBuilder.DropColumn(
            name: "RecordStatus",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");
    }
}
