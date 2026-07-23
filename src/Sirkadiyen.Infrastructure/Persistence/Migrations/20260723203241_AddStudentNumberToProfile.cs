using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddStudentNumberToProfile : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No default: the profile table is empty at this migration, and an empty
        // string could never satisfy the format check below, so a required
        // column with no server default is both correct and the cleaner result.
        migrationBuilder.AddColumn<string>(
            name: "StudentNumber",
            schema: "sirkadiyen",
            table: "student_profiles",
            type: "character varying(10)",
            maxLength: 10,
            nullable: false);

        migrationBuilder.AddCheckConstraint(
            name: "ck_student_profiles_student_number",
            schema: "sirkadiyen",
            table: "student_profiles",
            sql: "\"StudentNumber\" ~ '^[0-9]{10}$'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_student_profiles_student_number",
            schema: "sirkadiyen",
            table: "student_profiles");

        migrationBuilder.DropColumn(
            name: "StudentNumber",
            schema: "sirkadiyen",
            table: "student_profiles");
    }
}
