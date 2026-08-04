using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class RemoveServiceHeartbeats : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "service_heartbeats",
            schema: "sirkadiyen");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "service_heartbeats",
            schema: "sirkadiyen",
            columns: table => new
            {
                ServiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                InstanceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_service_heartbeats", x => x.ServiceName);
            });
    }
}
