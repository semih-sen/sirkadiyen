using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddStudentProfiles : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "student_profiles",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                AcademicYear = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                ClassYear = table.Column<int>(type: "integer", nullable: false),
                ProgramLanguage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SelectorSchemaVersion = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Selectors = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_student_profiles", x => x.Id);
                table.CheckConstraint("ck_student_profiles_class_year", "\"ClassYear\" BETWEEN 1 AND 6");
                table.CheckConstraint("ck_student_profiles_program_language", "\"ProgramLanguage\" IN ('Turkish', 'English')");
                table.ForeignKey(
                    name: "FK_student_profiles_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_student_profiles_UserId",
            schema: "sirkadiyen",
            table: "student_profiles",
            column: "UserId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "student_profiles",
            schema: "sirkadiyen");
    }
}
