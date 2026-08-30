using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSourcePollFailure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastPollFailureAtUtc",
                schema: "sirkadiyen",
                table: "schedule_sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastPollFailureReason",
                schema: "sirkadiyen",
                table: "schedule_sources",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPollFailureAtUtc",
                schema: "sirkadiyen",
                table: "schedule_sources");

            migrationBuilder.DropColumn(
                name: "LastPollFailureReason",
                schema: "sirkadiyen",
                table: "schedule_sources");
        }
    }
}
