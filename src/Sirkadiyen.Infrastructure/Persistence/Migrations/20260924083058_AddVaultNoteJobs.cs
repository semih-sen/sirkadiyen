using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVaultNoteJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "vault_note_jobs",
                schema: "sirkadiyen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    Prompt = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    Folder = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Title = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    NotePath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    NoteLink = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Backlinks = table.Column<string>(type: "jsonb", nullable: false),
                    Warnings = table.Column<string>(type: "jsonb", nullable: false),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_vault_note_jobs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_vault_note_jobs_CreatedAtUtc",
                schema: "sirkadiyen",
                table: "vault_note_jobs",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_vault_note_jobs_Status",
                schema: "sirkadiyen",
                table: "vault_note_jobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "vault_note_jobs",
                schema: "sirkadiyen");
        }
    }
}
