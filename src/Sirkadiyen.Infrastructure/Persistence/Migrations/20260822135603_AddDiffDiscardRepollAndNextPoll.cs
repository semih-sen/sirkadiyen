using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiffDiscardRepollAndNextPoll : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "NextSourcePollAtUtc",
                schema: "sirkadiyen",
                table: "worker_instances",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscardReason",
                schema: "sirkadiyen",
                table: "schedule_diffs",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DiscardedAtUtc",
                schema: "sirkadiyen",
                table: "schedule_diffs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DiscardedBy",
                schema: "sirkadiyen",
                table: "schedule_diffs",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "source_poll_requests",
                schema: "sirkadiyen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Force = table.Column<bool>(type: "boolean", nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClaimedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_source_poll_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_source_poll_requests_ClaimedAtUtc_RequestedAtUtc",
                schema: "sirkadiyen",
                table: "source_poll_requests",
                columns: new[] { "ClaimedAtUtc", "RequestedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "source_poll_requests",
                schema: "sirkadiyen");

            migrationBuilder.DropColumn(
                name: "NextSourcePollAtUtc",
                schema: "sirkadiyen",
                table: "worker_instances");

            migrationBuilder.DropColumn(
                name: "DiscardReason",
                schema: "sirkadiyen",
                table: "schedule_diffs");

            migrationBuilder.DropColumn(
                name: "DiscardedAtUtc",
                schema: "sirkadiyen",
                table: "schedule_diffs");

            migrationBuilder.DropColumn(
                name: "DiscardedBy",
                schema: "sirkadiyen",
                table: "schedule_diffs");
        }
    }
}
