using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupRotationOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An empty JSON array, not an empty string: jsonb refuses '' outright,
            // so the scaffolded CLR default would have failed on the first existing
            // row. Every source that predates the fallback owns its rotation
            // decision unconditionally, which is what '[]' says (ADR-126).
            migrationBuilder.AddColumn<string>(
                name: "GroupRotationSourceIds",
                schema: "sirkadiyen",
                table: "schedule_sources",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GroupRotationSourceIds",
                schema: "sirkadiyen",
                table: "schedule_sources");
        }
    }
}
