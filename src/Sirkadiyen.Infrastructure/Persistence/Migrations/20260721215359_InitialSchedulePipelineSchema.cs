using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialSchedulePipelineSchema : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "sirkadiyen");

        migrationBuilder.CreateTable(
            name: "schedule_sources",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Transport = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                DocumentFormat = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                SourceUri = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                ExternalId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                SheetGid = table.Column<long>(type: "bigint", nullable: true),
                ParserProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ParserProfileVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AcademicYear = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ClassYear = table.Column<int>(type: "integer", nullable: false),
                ProgramLanguage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                IsPollingEnabled = table.Column<bool>(type: "boolean", nullable: false),
                LastPolledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_sources", x => x.Id);
                table.CheckConstraint("ck_schedule_sources_class_year", "\"ClassYear\" BETWEEN 1 AND 6");
            });

        migrationBuilder.CreateTable(
            name: "source_snapshots",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduleSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ExternalSnapshotId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                SpreadsheetId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                AcquiredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ContentHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ContractVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Payload = table.Column<string>(type: "jsonb", nullable: false),
                WorksheetCount = table.Column<int>(type: "integer", nullable: false),
                CellCount = table.Column<int>(type: "integer", nullable: false),
                DiagnosticCount = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_source_snapshots", x => x.Id);
                table.ForeignKey(
                    name: "FK_source_snapshots_schedule_sources_ScheduleSourceId",
                    column: x => x.ScheduleSourceId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "parse_runs",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                ParserProfile = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ParserProfileVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CandidateCount = table.Column<int>(type: "integer", nullable: false),
                WarningCount = table.Column<int>(type: "integer", nullable: false),
                ErrorCount = table.Column<int>(type: "integer", nullable: false),
                Response = table.Column<string>(type: "jsonb", nullable: true),
                FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_parse_runs", x => x.Id);
                table.ForeignKey(
                    name: "FK_parse_runs_source_snapshots_SourceSnapshotId",
                    column: x => x.SourceSnapshotId,
                    principalSchema: "sirkadiyen",
                    principalTable: "source_snapshots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "schedule_revisions",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduleSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ParseRunId = table.Column<Guid>(type: "uuid", nullable: false),
                State = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                PublishedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SupersededAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SupersededByRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                StateReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                RecordCount = table.Column<int>(type: "integer", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_schedule_revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_schedule_revisions_parse_runs_ParseRunId",
                    column: x => x.ParseRunId,
                    principalSchema: "sirkadiyen",
                    principalTable: "parse_runs",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_schedule_revisions_schedule_sources_ScheduleSourceId",
                    column: x => x.ScheduleSourceId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "canonical_schedule_records",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduleRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AcademicYear = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ClassYear = table.Column<int>(type: "integer", nullable: false),
                ProgramLanguage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                EventType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                AudienceScope = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                AudienceSelectors = table.Column<string>(type: "jsonb", nullable: false),
                DisplayTitle = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                NormalizedCourseIdentity = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                StartLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                EndLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Instructor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Location = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                StableIdentity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ContentHash = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Confidence = table.Column<decimal>(type: "numeric(4,3)", precision: 4, scale: 3, nullable: false),
                Evidence = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_canonical_schedule_records", x => x.Id);
                table.CheckConstraint("ck_canonical_schedule_records_time_order", "\"EndLocalTime\" > \"StartLocalTime\"");
                table.ForeignKey(
                    name: "FK_canonical_schedule_records_schedule_revisions_ScheduleRevis~",
                    column: x => x.ScheduleRevisionId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_revisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_canonical_schedule_records_ScheduleRevisionId_LocalDate",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            columns: new[] { "ScheduleRevisionId", "LocalDate" });

        migrationBuilder.CreateIndex(
            name: "IX_canonical_schedule_records_ScheduleRevisionId_StableIdentity",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            columns: new[] { "ScheduleRevisionId", "StableIdentity" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_canonical_schedule_records_SourceId_ClassYear_ProgramLangua~",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            columns: new[] { "SourceId", "ClassYear", "ProgramLanguage", "LocalDate" });

        migrationBuilder.CreateIndex(
            name: "IX_parse_runs_SourceSnapshotId_ParserProfile_ParserProfileVers~",
            schema: "sirkadiyen",
            table: "parse_runs",
            columns: new[] { "SourceSnapshotId", "ParserProfile", "ParserProfileVersion" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_schedule_revisions_ParseRunId",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            column: "ParseRunId");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_revisions_ScheduleSourceId_CreatedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            columns: new[] { "ScheduleSourceId", "CreatedAtUtc" });

        migrationBuilder.CreateIndex(
            name: "ix_schedule_revisions_one_published_per_source",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            column: "ScheduleSourceId",
            unique: true,
            filter: "\"State\" = 'Published'");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_sources_IsPollingEnabled",
            schema: "sirkadiyen",
            table: "schedule_sources",
            column: "IsPollingEnabled",
            filter: "\"IsPollingEnabled\"");

        migrationBuilder.CreateIndex(
            name: "IX_schedule_sources_SourceId",
            schema: "sirkadiyen",
            table: "schedule_sources",
            column: "SourceId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_source_snapshots_ScheduleSourceId",
            schema: "sirkadiyen",
            table: "source_snapshots",
            column: "ScheduleSourceId");

        migrationBuilder.CreateIndex(
            name: "IX_source_snapshots_SourceId_AcquiredAtUtc",
            schema: "sirkadiyen",
            table: "source_snapshots",
            columns: new[] { "SourceId", "AcquiredAtUtc" },
            descending: new[] { false, true });

        migrationBuilder.CreateIndex(
            name: "IX_source_snapshots_SourceId_ContentHash",
            schema: "sirkadiyen",
            table: "source_snapshots",
            columns: new[] { "SourceId", "ContentHash" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "canonical_schedule_records",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "schedule_revisions",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "parse_runs",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "source_snapshots",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "schedule_sources",
            schema: "sirkadiyen");
    }
}
