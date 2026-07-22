using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddRevisionPublicationApproval : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ApprovalReason",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ApprovedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "ApprovedBy",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_schedule_revisions_State_CreatedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_revisions",
            columns: new[] { "State", "CreatedAtUtc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_schedule_revisions_State_CreatedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_revisions");

        migrationBuilder.DropColumn(
            name: "ApprovalReason",
            schema: "sirkadiyen",
            table: "schedule_revisions");

        migrationBuilder.DropColumn(
            name: "ApprovedAtUtc",
            schema: "sirkadiyen",
            table: "schedule_revisions");

        migrationBuilder.DropColumn(
            name: "ApprovedBy",
            schema: "sirkadiyen",
            table: "schedule_revisions");
    }
}
