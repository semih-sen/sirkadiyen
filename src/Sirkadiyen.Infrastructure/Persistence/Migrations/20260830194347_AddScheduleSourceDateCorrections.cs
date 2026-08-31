using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleSourceDateCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_source_date_corrections",
                schema: "sirkadiyen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Original = table.Column<DateOnly>(type: "date", nullable: false),
                    Corrected = table.Column<DateOnly>(type: "date", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    DecidedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_source_date_corrections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_source_date_corrections_SourceId_Original",
                schema: "sirkadiyen",
                table: "schedule_source_date_corrections",
                columns: new[] { "SourceId", "Original" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_source_date_corrections",
                schema: "sirkadiyen");
        }
    }
}
