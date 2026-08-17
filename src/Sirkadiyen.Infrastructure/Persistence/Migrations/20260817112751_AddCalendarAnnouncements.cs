using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCalendarAnnouncements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "calendar_announcements",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                CampaignKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                TemplateKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                IsAllDay = table.Column<bool>(type: "boolean", nullable: false),
                LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                StartLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                EndLocalTime = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ReminderMinutesBefore = table.Column<int>(type: "integer", nullable: true),
                CategoryKey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AudienceAcademicYear = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AudienceClassYear = table.Column<int>(type: "integer", nullable: true),
                AudienceProgramLanguage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                AudienceSelectors = table.Column<string>(type: "jsonb", nullable: false),
                TargetUserId = table.Column<Guid>(type: "uuid", nullable: true),
                InternalNote = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                ContentVersion = table.Column<int>(type: "integer", nullable: false),
                PlanHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                RecipientCount = table.Column<int>(type: "integer", nullable: false),
                ExcludedCount = table.Column<int>(type: "integer", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                CreationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastUpdatedBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                LastUpdateReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CancelledBy = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                CancellationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CancellationRequestedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CancelledAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DeliveryAttempts = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastFailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_calendar_announcements", x => x.Id);
                table.CheckConstraint("ck_calendar_announcements_target", "(\"Kind\" = 'UserWarning' AND \"TargetUserId\" IS NOT NULL) OR (\"Kind\" = 'Bulk' AND \"TargetUserId\" IS NULL)");
                table.CheckConstraint("ck_calendar_announcements_times", "(\"IsAllDay\" AND \"StartLocalTime\" IS NULL AND \"EndLocalTime\" IS NULL) OR (NOT \"IsAllDay\" AND \"StartLocalTime\" IS NOT NULL AND \"EndLocalTime\" IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_calendar_announcements_users_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_calendar_announcements_users_TargetUserId",
                    column: x => x.TargetUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "calendar_announcement_deliveries",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CalendarAnnouncementId = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                GoogleCalendarId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                GoogleEventId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                AppliedContentVersion = table.Column<int>(type: "integer", nullable: true),
                SkipReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_calendar_announcement_deliveries", x => x.Id);
                table.CheckConstraint("ck_calendar_announcement_deliveries_skip_reason", "(\"State\" <> 'Skipped') OR (\"SkipReason\" IS NOT NULL)");
                table.ForeignKey(
                    name: "FK_calendar_announcement_deliveries_calendar_announcements_Cal~",
                    column: x => x.CalendarAnnouncementId,
                    principalSchema: "sirkadiyen",
                    principalTable: "calendar_announcements",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_calendar_announcement_deliveries_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_calendar_announcement_deliveries_CalendarAnnouncementId_Sta~",
            schema: "sirkadiyen",
            table: "calendar_announcement_deliveries",
            columns: new[] { "CalendarAnnouncementId", "State" });

        migrationBuilder.CreateIndex(
            name: "IX_calendar_announcement_deliveries_CalendarAnnouncementId_Use~",
            schema: "sirkadiyen",
            table: "calendar_announcement_deliveries",
            columns: new[] { "CalendarAnnouncementId", "UserId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_calendar_announcement_deliveries_UserId",
            schema: "sirkadiyen",
            table: "calendar_announcement_deliveries",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_calendar_announcements_CampaignKey",
            schema: "sirkadiyen",
            table: "calendar_announcements",
            column: "CampaignKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_calendar_announcements_CreatedByUserId",
            schema: "sirkadiyen",
            table: "calendar_announcements",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_calendar_announcements_Status_NextAttemptAtUtc",
            schema: "sirkadiyen",
            table: "calendar_announcements",
            columns: new[] { "Status", "NextAttemptAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_calendar_announcements_TargetUserId",
            schema: "sirkadiyen",
            table: "calendar_announcements",
            column: "TargetUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "calendar_announcement_deliveries",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "calendar_announcements",
            schema: "sirkadiyen");
    }
}
