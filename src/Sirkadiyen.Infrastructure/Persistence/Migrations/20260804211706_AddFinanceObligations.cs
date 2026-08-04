using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddFinanceObligations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "finance_obligations",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CounterpartyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                SettledAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                WrittenOffOn = table.Column<DateOnly>(type: "date", nullable: true),
                CancelledOn = table.Column<DateOnly>(type: "date", nullable: true),
                ClosureReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_obligations", x => x.Id);
                table.CheckConstraint("ck_finance_obligations_amount", "\"Amount\" > 0");
                table.CheckConstraint("ck_finance_obligations_dates", "\"DueOn\" IS NULL OR \"DueOn\" >= \"IssuedOn\"");
                table.CheckConstraint("ck_finance_obligations_direction", "\"Direction\" IN ('Receivable', 'Payable')");
                table.CheckConstraint("ck_finance_obligations_direction_category", "(\"Direction\" = 'Receivable' AND \"Category\" IN ('LicenseSales', 'Sponsorship', 'Donation', 'OtherIncome'))\nOR (\"Direction\" = 'Payable' AND \"Category\" IN ('Servers', 'Domains', 'ExternalServices',\n                                'SoftwareLicenses', 'Marketing', 'Operational', 'Charitable', 'OtherExpense'))");
                table.CheckConstraint("ck_finance_obligations_settled", "\"SettledAmount\" >= 0 AND \"SettledAmount\" <= \"Amount\"");
                table.CheckConstraint("ck_finance_obligations_status", "(\"Status\" = 'Open'             AND \"SettledAmount\" = 0)\nOR (\"Status\" = 'PartiallySettled' AND \"SettledAmount\" > 0 AND \"SettledAmount\" < \"Amount\")\nOR (\"Status\" = 'Settled'          AND \"SettledAmount\" = \"Amount\")\nOR (\"Status\" = 'WrittenOff'       AND \"WrittenOffOn\" IS NOT NULL AND \"ClosureReason\" IS NOT NULL)\nOR (\"Status\" = 'Cancelled'        AND \"CancelledOn\" IS NOT NULL AND \"ClosureReason\" IS NOT NULL\n                                   AND \"SettledAmount\" = 0)");
                table.ForeignKey(
                    name: "FK_finance_obligations_users_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "finance_settlements",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FinanceObligationId = table.Column<Guid>(type: "uuid", nullable: false),
                FinanceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                Direction = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                SettledOn = table.Column<DateOnly>(type: "date", nullable: false),
                RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_settlements", x => x.Id);
                table.CheckConstraint("ck_finance_settlements_amount", "\"Amount\" > 0");
                table.CheckConstraint("ck_finance_settlements_direction", "\"Direction\" IN ('Receivable', 'Payable')");
                table.ForeignKey(
                    name: "FK_finance_settlements_finance_obligations_FinanceObligationId",
                    column: x => x.FinanceObligationId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_obligations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_finance_settlements_finance_transactions_FinanceTransaction~",
                    column: x => x.FinanceTransactionId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_finance_obligations_CreatedByUserId",
            schema: "sirkadiyen",
            table: "finance_obligations",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_finance_obligations_Direction_Status_DueOn",
            schema: "sirkadiyen",
            table: "finance_obligations",
            columns: new[] { "Direction", "Status", "DueOn" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_obligations_IssuedOn",
            schema: "sirkadiyen",
            table: "finance_obligations",
            column: "IssuedOn");

        migrationBuilder.CreateIndex(
            name: "IX_finance_settlements_Direction_SettledOn",
            schema: "sirkadiyen",
            table: "finance_settlements",
            columns: new[] { "Direction", "SettledOn" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_settlements_FinanceObligationId_FinanceTransactionId",
            schema: "sirkadiyen",
            table: "finance_settlements",
            columns: new[] { "FinanceObligationId", "FinanceTransactionId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_settlements_FinanceTransactionId",
            schema: "sirkadiyen",
            table: "finance_settlements",
            column: "FinanceTransactionId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "finance_settlements",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "finance_obligations",
            schema: "sirkadiyen");
    }
}
