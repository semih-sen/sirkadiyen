using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSourceDocumentUploads : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SharedDocumentGroup",
            schema: "sirkadiyen",
            table: "schedule_sources",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "source_document_uploads",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SourceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ScheduleSourceId = table.Column<Guid>(type: "uuid", nullable: false),
                UploadedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                ByteCount = table.Column<long>(type: "bigint", nullable: false),
                ContentSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                SnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                UploadedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_source_document_uploads", x => x.Id);
                table.ForeignKey(
                    name: "FK_source_document_uploads_schedule_sources_ScheduleSourceId",
                    column: x => x.ScheduleSourceId,
                    principalSchema: "sirkadiyen",
                    principalTable: "schedule_sources",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_source_document_uploads_source_snapshots_SnapshotId",
                    column: x => x.SnapshotId,
                    principalSchema: "sirkadiyen",
                    principalTable: "source_snapshots",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_schedule_sources_SharedDocumentGroup",
            schema: "sirkadiyen",
            table: "schedule_sources",
            column: "SharedDocumentGroup",
            filter: "\"SharedDocumentGroup\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_source_document_uploads_ScheduleSourceId",
            schema: "sirkadiyen",
            table: "source_document_uploads",
            column: "ScheduleSourceId");

        migrationBuilder.CreateIndex(
            name: "IX_source_document_uploads_SnapshotId",
            schema: "sirkadiyen",
            table: "source_document_uploads",
            column: "SnapshotId");

        migrationBuilder.CreateIndex(
            name: "IX_source_document_uploads_SourceId_UploadedAtUtc",
            schema: "sirkadiyen",
            table: "source_document_uploads",
            columns: new[] { "SourceId", "UploadedAtUtc" },
            descending: new[] { false, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "source_document_uploads",
            schema: "sirkadiyen");

        migrationBuilder.DropIndex(
            name: "IX_schedule_sources_SharedDocumentGroup",
            schema: "sirkadiyen",
            table: "schedule_sources");

        migrationBuilder.DropColumn(
            name: "SharedDocumentGroup",
            schema: "sirkadiyen",
            table: "schedule_sources");
    }
}
