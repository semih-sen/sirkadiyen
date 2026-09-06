using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCafeteriaMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "meal_calendar_deliveries",
                schema: "sirkadiyen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    GoogleCalendarId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    GoogleEventId = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AppliedContentVersion = table.Column<int>(type: "integer", nullable: true),
                    SkipReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    FailureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_calendar_deliveries", x => x.Id);
                    table.CheckConstraint("ck_meal_calendar_deliveries_skip_reason", "(\"State\" <> 'Skipped') OR (\"SkipReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_meal_calendar_deliveries_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "sirkadiyen",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "meal_menu_days",
                schema: "sirkadiyen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LocalDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MealText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ContentVersion = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ConsecutiveMissCount = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastConfirmedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastChangedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    WithdrawnAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_menu_days", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "meal_menu_subscriptions",
                schema: "sirkadiyen",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_meal_menu_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_meal_menu_subscriptions_users_UserId",
                        column: x => x.UserId,
                        principalSchema: "sirkadiyen",
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_meal_calendar_deliveries_Category_State_LocalDate",
                schema: "sirkadiyen",
                table: "meal_calendar_deliveries",
                columns: new[] { "Category", "State", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_meal_calendar_deliveries_UserId_LocalDate_Category",
                schema: "sirkadiyen",
                table: "meal_calendar_deliveries",
                columns: new[] { "UserId", "LocalDate", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meal_menu_days_Category_Status_LocalDate",
                schema: "sirkadiyen",
                table: "meal_menu_days",
                columns: new[] { "Category", "Status", "LocalDate" });

            migrationBuilder.CreateIndex(
                name: "IX_meal_menu_days_LocalDate_Category",
                schema: "sirkadiyen",
                table: "meal_menu_days",
                columns: new[] { "LocalDate", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_meal_menu_subscriptions_UserId",
                schema: "sirkadiyen",
                table: "meal_menu_subscriptions",
                column: "UserId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "meal_calendar_deliveries",
                schema: "sirkadiyen");

            migrationBuilder.DropTable(
                name: "meal_menu_days",
                schema: "sirkadiyen");

            migrationBuilder.DropTable(
                name: "meal_menu_subscriptions",
                schema: "sirkadiyen");
        }
    }
}
