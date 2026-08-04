using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddScopedFreezeAndServiceHeartbeats : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "scoped_operational_freeze_controls",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ClassYear = table.Column<int>(type: "integer", nullable: false),
                ProgramLanguage = table.Column<string>(type: "text", nullable: false),
                IsFrozen = table.Column<bool>(type: "boolean", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scoped_operational_freeze_controls", x => x.Id);
                table.CheckConstraint("ck_scoped_operational_freeze_class_year", "\"ClassYear\" BETWEEN 1 AND 6");
            });

        migrationBuilder.CreateTable(
            name: "service_heartbeats",
            schema: "sirkadiyen",
            columns: table => new
            {
                ServiceName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                InstanceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                StartedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_service_heartbeats", x => x.ServiceName);
            });

        migrationBuilder.CreateTable(
            name: "scoped_operational_freeze_audits",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ScopedOperationalFreezeControlId = table.Column<Guid>(type: "uuid", nullable: false),
                IsFrozen = table.Column<bool>(type: "boolean", nullable: false),
                ChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_scoped_operational_freeze_audits", x => x.Id);
                table.ForeignKey(
                    name: "FK_scoped_operational_freeze_audits_scoped_operational_freeze_~",
                    column: x => x.ScopedOperationalFreezeControlId,
                    principalSchema: "sirkadiyen",
                    principalTable: "scoped_operational_freeze_controls",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_scoped_operational_freeze_audits_ChangedAtUtc",
            schema: "sirkadiyen",
            table: "scoped_operational_freeze_audits",
            column: "ChangedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_scoped_operational_freeze_audits_ScopedOperationalFreezeCon~",
            schema: "sirkadiyen",
            table: "scoped_operational_freeze_audits",
            column: "ScopedOperationalFreezeControlId");

        migrationBuilder.CreateIndex(
            name: "IX_scoped_operational_freeze_controls_ClassYear_ProgramLanguage",
            schema: "sirkadiyen",
            table: "scoped_operational_freeze_controls",
            columns: new[] { "ClassYear", "ProgramLanguage" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "scoped_operational_freeze_audits",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "service_heartbeats",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "scoped_operational_freeze_controls",
            schema: "sirkadiyen");
    }
}
