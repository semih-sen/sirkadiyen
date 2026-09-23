using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRetiredRemovalAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredRemovalAuthorizedAtUtc",
                schema: "sirkadiyen",
                table: "google_calendar_connections",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_google_calendar_connections_retired_removal",
                schema: "sirkadiyen",
                table: "google_calendar_connections",
                sql: "\"RetiredRemovalAuthorizedAtUtc\" IS NULL OR \"ProfileResyncRequiredSinceUtc\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_google_calendar_connections_retired_removal",
                schema: "sirkadiyen",
                table: "google_calendar_connections");

            migrationBuilder.DropColumn(
                name: "RetiredRemovalAuthorizedAtUtc",
                schema: "sirkadiyen",
                table: "google_calendar_connections");
        }
    }
}
