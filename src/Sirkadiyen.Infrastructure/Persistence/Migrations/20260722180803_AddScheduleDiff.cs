using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the stored semantic diff between a published revision and the revision
/// it superseded, plus its per-record entries. Both tables are new; nothing
/// existing is altered.
/// </summary>
public sealed partial class AddScheduleDiff : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "schedule_diffs",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduleSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PreviousRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                HoldReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedCount = table.Column<int>(type: "integer", nullable: false),
                UpdatedCount = table.Column<int>(type: "integer", nullable: false),
                DeletedCount = table.Column<int>(type: "integer", nullable: false),
                UnchangedCount = table.Column<int>(type: "integer", nullable: false),
                AmbiguousCount = table.Column<int>(type: "integer", nullable: false),
                PreviousRecordCount = table.Column<int>(type: "integer", nullable: false),
                CurrentRecordCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_diffs", x => x.Id);
                table.ForeignKey(
                    name: "FK_schedule_diffs_schedule_revisions_CurrentRevisionId",
                    column: x => x.CurrentRevisionId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_revisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_schedule_diffs_schedule_revisions_PreviousRevisionId",
                    column: x => x.PreviousRevisionId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_revisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_schedule_diffs_schedule_sources_ScheduleSourceId",
                    column: x => x.ScheduleSourceId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "schedule_diff_entries",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduleDiffId = table.Column<Guid>(type: "uuid", nullable: false),
                Change = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Match = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                PreviousRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                CurrentRecordId = table.Column<Guid>(type: "uuid", nullable: true),
                MatchScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                TitleScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                InstructorScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                DepartmentScore = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_diff_entries", x => x.Id);
                table.CheckConstraint("ck_schedule_diff_entries_record_presence", "\"PreviousRecordId\" IS NOT NULL OR \"CurrentRecordId\" IS NOT NULL");
                table.ForeignKey(
                    name: "FK_schedule_diff_entries_canonical_schedule_records_CurrentRec~",
                    column: x => x.CurrentRecordId,
                    principalSchema: "sirkadiyen",
                    principalTable: "canonical_schedule_records",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_schedule_diff_entries_canonical_schedule_records_PreviousRe~",
                    column: x => x.PreviousRecordId,
                    principalSchema: "sirkadiyen",
                    principalTable: "canonical_schedule_records",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_schedule_diff_entries_schedule_diffs_ScheduleDiffId",
                    column: x => x.ScheduleDiffId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_diffs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diff_entries_CurrentRecordId",
            schema: "sirkadiyen",
            table: "schedule_diff_entries",
            column: "CurrentRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diff_entries_PreviousRecordId",
            schema: "sirkadiyen",
            table: "schedule_diff_entries",
            column: "PreviousRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diff_entries_ScheduleDiffId_Change",
            schema: "sirkadiyen",
            table: "schedule_diff_entries",
            columns: new[] { "ScheduleDiffId", "Change" });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diff_entries_ScheduleDiffId_CurrentRecordId",
            schema: "sirkadiyen",
            table: "schedule_diff_entries",
            columns: new[] { "ScheduleDiffId", "CurrentRecordId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diff_entries_ScheduleDiffId_PreviousRecordId",
            schema: "sirkadiyen",
            table: "schedule_diff_entries",
            columns: new[] { "ScheduleDiffId", "PreviousRecordId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diffs_CurrentRevisionId",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            column: "CurrentRevisionId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diffs_PreviousRevisionId",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            column: "PreviousRevisionId");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diffs_ScheduleSourceId",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            column: "ScheduleSourceId");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_diffs_State_CreatedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            columns: new[] { "State", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "schedule_diff_entries",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "schedule_diffs",
            schema: "sirkadiyen");
    }
}
