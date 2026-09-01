using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRevisionRecordSetHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecordSetHash",
                schema: "sirkadiyen",
                table: "schedule_revisions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecordSetHash",
                schema: "sirkadiyen",
                table: "schedule_revisions");
        }
    }
}
