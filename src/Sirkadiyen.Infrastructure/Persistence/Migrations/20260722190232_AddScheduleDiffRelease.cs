using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the audit trail for an operator releasing a held diff (ADR-042). All
/// three columns are nullable additions; existing diffs stay untouched, and a
/// diff that was never held keeps them null forever.
/// </summary>
/// <remarks>
/// The row version enabled alongside them is PostgreSQL's system <c>xmin</c>
/// column, so it adds no storage and emits no statement.
/// </remarks>
public sealed partial class AddScheduleDiffRelease : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ReleaseReason",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ReleasedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ReleasedBy",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.AddColumn<uint>(
            name: "xmin",
            schema: "sirkadiyen",
            table: "schedule_diffs",
            type: "xid",
            rowVersion: true,
            nullable: false,
            defaultValue: 0u);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReleaseReason",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "ReleasedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "ReleasedBy",
            schema: "sirkadiyen",
            table: "schedule_diffs");

        migrationBuilder.DropColumn(
            name: "xmin",
            schema: "sirkadiyen",
            table: "schedule_diffs");
    }
}
