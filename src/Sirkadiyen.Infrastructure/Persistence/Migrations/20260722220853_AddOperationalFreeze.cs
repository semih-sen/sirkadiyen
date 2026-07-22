using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddOperationalFreeze : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "operational_freeze_control",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false),
                IsFrozen = table.Column<bool>(type: "boolean", nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_operational_freeze_control", x => x.Id);
                table.CheckConstraint("ck_operational_freeze_control_singleton", "\"Id\" = 1");
            });

        migrationBuilder.CreateTable(
            name: "operational_freeze_audits",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperationalFreezeControlId = table.Column<int>(type: "integer", nullable: false),
                IsFrozen = table.Column<bool>(type: "boolean", nullable: false),
                ChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_operational_freeze_audits", x => x.Id);
                table.ForeignKey(
                    name: "FK_operational_freeze_audits_operational_freeze_control_Operat~",
                    column: x => x.OperationalFreezeControlId,
                    principalSchema: "sirkadiyen",
                    principalTable: "operational_freeze_control",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.InsertData(
            schema: "sirkadiyen",
            table: "operational_freeze_control",
            columns: new[] { "Id", "ChangedAtUtc", "ChangedBy", "CorrelationId", "IsFrozen", "Reason" },
            values: new object[] { 1, null, null, null, false, null });

        migrationBuilder.CreateIndex(
            name: "IX_operational_freeze_audits_ChangedAtUtc",
            schema: "sirkadiyen",
            table: "operational_freeze_audits",
            column: "ChangedAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_operational_freeze_audits_OperationalFreezeControlId",
            schema: "sirkadiyen",
            table: "operational_freeze_audits",
            column: "OperationalFreezeControlId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "operational_freeze_audits",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "operational_freeze_control",
            schema: "sirkadiyen");
    }
}
