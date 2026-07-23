using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddGoogleCalendarConnections : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "google_calendar_connections",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                ProtectedRefreshToken = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                GrantedScopes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                ManagedCalendarId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_google_calendar_connections", x => x.Id);
                table.CheckConstraint("ck_google_calendar_connections_status", "\"Status\" IN ('Authorized', 'NeedsReauthorization')");
                table.ForeignKey(
                    name: "FK_google_calendar_connections_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_google_calendar_connections_UserId",
            schema: "sirkadiyen",
            table: "google_calendar_connections",
            column: "UserId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "google_calendar_connections",
            schema: "sirkadiyen");
    }
}
