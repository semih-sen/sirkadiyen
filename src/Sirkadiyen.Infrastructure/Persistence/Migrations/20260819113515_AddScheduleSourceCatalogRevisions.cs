using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleSourceCatalogRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "schedule_source_catalog_revisions",
                schema: "sirkadiyen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PreviousContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SourceCount = table.Column<int>(type: "integer", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ChangeSummary = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_schedule_source_catalog_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_schedule_source_catalog_revisions_users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalSchema: "sirkadiyen",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_schedule_source_catalog_revisions_ActorUserId",
                schema: "sirkadiyen",
                table: "schedule_source_catalog_revisions",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_source_catalog_revisions_ContentHash",
                schema: "sirkadiyen",
                table: "schedule_source_catalog_revisions",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_schedule_source_catalog_revisions_RecordedAtUtc",
                schema: "sirkadiyen",
                table: "schedule_source_catalog_revisions",
                column: "RecordedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "schedule_source_catalog_revisions",
                schema: "sirkadiyen");
        }
    }
}
