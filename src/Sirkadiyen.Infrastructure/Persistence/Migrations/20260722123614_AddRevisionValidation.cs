using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddRevisionValidation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SupportedAudienceSelectors",
            schema: "sirkadiyen",
            table: "schedule_sources",
            type: "jsonb",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "revision_validation_findings",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScheduleRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                Rule = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                Severity = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                Detail = table.Column<string>(type: "jsonb", nullable: false),
                AffectedRecordCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_revision_validation_findings", x => x.Id);
                table.ForeignKey(
                    name: "FK_revision_validation_findings_schedule_revisions_ScheduleRev~",
                    column: x => x.ScheduleRevisionId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_revisions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_revision_validation_findings_ScheduleRevisionId_CreatedAtUtc",
            schema: "sirkadiyen",
            table: "revision_validation_findings",
            columns: new[] { "ScheduleRevisionId", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "revision_validation_findings",
            schema: "sirkadiyen");

        migrationBuilder.DropColumn(
            name: "SupportedAudienceSelectors",
            schema: "sirkadiyen",
            table: "schedule_sources");
    }
}
