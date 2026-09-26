using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddVaultFlashcardJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Flashcards",
                schema: "sirkadiyen",
                table: "vault_note_jobs",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                schema: "sirkadiyen",
                table: "vault_note_jobs",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Every job before this migration wrote a new note.
                defaultValue: "Note");

            migrationBuilder.AddColumn<string>(
                name: "TargetPath",
                schema: "sirkadiyen",
                table: "vault_note_jobs",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Flashcards",
                schema: "sirkadiyen",
                table: "vault_note_jobs");

            migrationBuilder.DropColumn(
                name: "Kind",
                schema: "sirkadiyen",
                table: "vault_note_jobs");

            migrationBuilder.DropColumn(
                name: "TargetPath",
                schema: "sirkadiyen",
                table: "vault_note_jobs");
        }
    }
}
