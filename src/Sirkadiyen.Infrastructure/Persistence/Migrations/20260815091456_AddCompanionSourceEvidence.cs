using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCompanionSourceEvidence : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_parse_runs_SourceSnapshotId_ParserProfile_ParserProfileVers~",
            schema: "sirkadiyen",
            table: "parse_runs");

        // The default is an empty JSON array, not an empty string: jsonb
        // refuses '' outright, so the scaffolded CLR default would have failed
        // on the first existing row. Every source that predates companions
        // names none, which is exactly what '[]' says.
        migrationBuilder.AddColumn<string>(
            name: "CompanionSourceIds",
            schema: "sirkadiyen",
            table: "schedule_sources",
            type: "jsonb",
            nullable: false,
            defaultValue: "[]");

        // Every existing run read no companion, and the empty string is what
        // "read none" is spelled as. It is deliberately not nullable: this
        // column joins the uniqueness key below, and PostgreSQL treats NULLs
        // in a unique index as distinct, which would permit exactly the
        // duplicate runs the key exists to forbid (ADR-102).
        migrationBuilder.AddColumn<string>(
            name: "CompanionFingerprint",
            schema: "sirkadiyen",
            table: "parse_runs",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_parse_runs_SourceSnapshotId_ParserProfile_ParserProfileVers~",
            schema: "sirkadiyen",
            table: "parse_runs",
            columns: new[] { "SourceSnapshotId", "ParserProfile", "ParserProfileVersion", "CompanionFingerprint" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_parse_runs_SourceSnapshotId_ParserProfile_ParserProfileVers~",
            schema: "sirkadiyen",
            table: "parse_runs");

        migrationBuilder.DropColumn(
            name: "CompanionSourceIds",
            schema: "sirkadiyen",
            table: "schedule_sources");

        migrationBuilder.DropColumn(
            name: "CompanionFingerprint",
            schema: "sirkadiyen",
            table: "parse_runs");

        migrationBuilder.CreateIndex(
            name: "IX_parse_runs_SourceSnapshotId_ParserProfile_ParserProfileVers~",
            schema: "sirkadiyen",
            table: "parse_runs",
            columns: new[] { "SourceSnapshotId", "ParserProfile", "ParserProfileVersion" },
            unique: true);
    }
}
