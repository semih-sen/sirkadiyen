using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiffCalculationRetry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DiffAttempts",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "DiffFailureReason",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DiffRetriedAtUtc",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiffRetriedBy",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiffRetryReason",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // "Pending", not the empty string EF scaffolds: every revision that already exists is
            // one whose diff calculation has never given up, and an empty value would both fail
            // ck_schedule_revisions_diff_state below and hide every existing undiffed revision
            // from the calculation queue (ADR-164).
            migrationBuilder.AddColumn<string>(
                name: "DiffState",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Pending");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextDiffAttemptAtUtc",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_schedule_revisions_DiffState_NextDiffAttemptAtUtc",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                columns: new[] { "DiffState", "NextDiffAttemptAtUtc" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_schedule_revisions_diff_attempts",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                sql: "\"DiffAttempts\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_schedule_revisions_diff_state",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                sql: "\"DiffState\" IN ('Pending', 'Failed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_schedule_revisions_DiffState_NextDiffAttemptAtUtc",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_revisions_diff_attempts",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_revisions_diff_state",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropColumn(
                name: "DiffAttempts",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropColumn(
                name: "DiffFailureReason",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropColumn(
                name: "DiffRetriedAtUtc",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropColumn(
                name: "DiffRetriedBy",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropColumn(
                name: "DiffRetryReason",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropColumn(
                name: "DiffState",
                schema: "sirkadiyen",
                table: "schedule_revisions");

            migrationBuilder.DropColumn(
                name: "NextDiffAttemptAtUtc",
                schema: "sirkadiyen",
                table: "schedule_revisions");
        }
    }
}
