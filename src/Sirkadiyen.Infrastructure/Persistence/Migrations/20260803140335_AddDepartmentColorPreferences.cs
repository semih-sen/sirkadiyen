using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDepartmentColorPreferences : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "department_color_audits",
            schema: "sirkadiyen",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                scope = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                department_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                previous_color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: true),
                new_color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: true),
                actor = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                correlation_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                changed_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_department_color_audits", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "department_color_settings",
            schema: "sirkadiyen",
            columns: table => new
            {
                department_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                background_color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                updated_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_department_color_settings", x => x.department_key);
                table.CheckConstraint("ck_department_color_settings_color", "background_color ~ '^#[0-9A-F]{6}$'");
            });

        migrationBuilder.CreateTable(
            name: "user_department_color_preferences",
            schema: "sirkadiyen",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                department_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                background_color = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                updated_at_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_department_color_preferences", x => new { x.user_id, x.department_key });
                table.CheckConstraint("ck_user_department_color_preferences_color", "background_color ~ '^#[0-9A-F]{6}$'");
                table.ForeignKey(
                    name: "FK_user_department_color_preferences_users_user_id",
                    column: x => x.user_id,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_department_color_audits_changed_at_utc",
            schema: "sirkadiyen",
            table: "department_color_audits",
            column: "changed_at_utc");

        migrationBuilder.CreateIndex(
            name: "IX_department_color_audits_user_id_changed_at_utc",
            schema: "sirkadiyen",
            table: "department_color_audits",
            columns: new[] { "user_id", "changed_at_utc" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "department_color_audits",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "department_color_settings",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "user_department_color_preferences",
            schema: "sirkadiyen");
    }
}
