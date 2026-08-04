using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddAuditEvents : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "audit_events",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ActorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                SubjectType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                MaskedIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ProtectedIp = table.Column<string>(type: "text", nullable: true),
                UserAgent = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Metadata = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_events", x => x.Id);
                table.ForeignKey(
                    name: "FK_audit_events_users_ActorUserId",
                    column: x => x.ActorUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_ActorUserId",
            schema: "sirkadiyen",
            table: "audit_events",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_Category",
            schema: "sirkadiyen",
            table: "audit_events",
            column: "Category");

        migrationBuilder.CreateIndex(
            name: "IX_audit_events_OccurredAtUtc",
            schema: "sirkadiyen",
            table: "audit_events",
            column: "OccurredAtUtc");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_events",
            schema: "sirkadiyen");
    }
}
