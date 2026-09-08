using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleSourcePublishesSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // True for every existing row: all but three of the catalogued sources publish a
            // schedule, and the three that do not are declared by the catalog this release ships,
            // which is applied at worker start (ADR-138, ADR-156).
            migrationBuilder.AddColumn<bool>(
                name: "PublishesSchedule",
                schema: "sirkadiyen",
                table: "schedule_sources",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublishesSchedule",
                schema: "sirkadiyen",
                table: "schedule_sources");
        }
    }
}
