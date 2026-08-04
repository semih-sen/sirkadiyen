using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddFinanceDistributions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "finance_distributions",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PeriodStartOn = table.Column<DateOnly>(type: "date", nullable: false),
                PeriodEndOn = table.Column<DateOnly>(type: "date", nullable: false),
                SourceFinanceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                DistributableAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ConfirmationToken = table.Column<Guid>(type: "uuid", nullable: false),
                PlanHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                ExecutedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ExecutedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                ExecutedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ReversedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                ReversedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                ReversalReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ReversedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_distributions", x => x.Id);
                table.CheckConstraint("ck_finance_distributions_amount", "\"DistributableAmount\" > 0");
                table.CheckConstraint("ck_finance_distributions_period", "\"PeriodEndOn\" >= \"PeriodStartOn\"");
                table.CheckConstraint("ck_finance_distributions_reversal", "(\"Status\" = 'Reversed'\n AND \"ReversedByUserId\" IS NOT NULL\n AND \"ReversedByEmail\" IS NOT NULL\n AND \"ReversalReason\" IS NOT NULL\n AND \"ReversedAtUtc\" IS NOT NULL)\nOR\n(\"Status\" <> 'Reversed'\n AND \"ReversedByUserId\" IS NULL\n AND \"ReversedByEmail\" IS NULL\n AND \"ReversalReason\" IS NULL\n AND \"ReversedAtUtc\" IS NULL)");
                table.CheckConstraint("ck_finance_distributions_status", "\"Status\" IN ('Executed', 'Reversed')");
                table.ForeignKey(
                    name: "FK_finance_distributions_finance_accounts_SourceFinanceAccount~",
                    column: x => x.SourceFinanceAccountId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_finance_distributions_users_ExecutedByUserId",
                    column: x => x.ExecutedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_finance_distributions_users_ReversedByUserId",
                    column: x => x.ReversedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "profit_distribution_shares",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FinanceDistributionId = table.Column<Guid>(type: "uuid", nullable: false),
                FinanceAccountHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                ShareBasisPoints = table.Column<int>(type: "integer", nullable: false),
                ExactShareMinorUnits = table.Column<long>(type: "bigint", nullable: false),
                AllocatedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                RemainderUnitAwarded = table.Column<bool>(type: "boolean", nullable: false),
                FinanceTransactionId = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_profit_distribution_shares", x => x.Id);
                table.CheckConstraint("ck_profit_distribution_shares_amount", "\"AllocatedAmount\" > 0");
                table.CheckConstraint("ck_profit_distribution_shares_basis_points", "\"ShareBasisPoints\" BETWEEN 1 AND 10000");
                table.CheckConstraint("ck_profit_distribution_shares_exact", "\"ExactShareMinorUnits\" >= 0");
                table.ForeignKey(
                    name: "FK_profit_distribution_shares_finance_account_holders_FinanceA~",
                    column: x => x.FinanceAccountHolderId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_account_holders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_profit_distribution_shares_finance_distributions_FinanceDis~",
                    column: x => x.FinanceDistributionId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_distributions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_profit_distribution_shares_finance_transactions_FinanceTran~",
                    column: x => x.FinanceTransactionId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_finance_distributions_ConfirmationToken",
            schema: "sirkadiyen",
            table: "finance_distributions",
            column: "ConfirmationToken",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_distributions_ExecutedByUserId",
            schema: "sirkadiyen",
            table: "finance_distributions",
            column: "ExecutedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_finance_distributions_PeriodStartOn_PeriodEndOn",
            schema: "sirkadiyen",
            table: "finance_distributions",
            columns: new[] { "PeriodStartOn", "PeriodEndOn" },
            unique: true,
            filter: "\"Status\" = 'Executed'");

        migrationBuilder.CreateIndex(
            name: "IX_finance_distributions_ReversedByUserId",
            schema: "sirkadiyen",
            table: "finance_distributions",
            column: "ReversedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_finance_distributions_SourceFinanceAccountId",
            schema: "sirkadiyen",
            table: "finance_distributions",
            column: "SourceFinanceAccountId");

        migrationBuilder.CreateIndex(
            name: "IX_profit_distribution_shares_FinanceAccountHolderId",
            schema: "sirkadiyen",
            table: "profit_distribution_shares",
            column: "FinanceAccountHolderId");

        migrationBuilder.CreateIndex(
            name: "IX_profit_distribution_shares_FinanceDistributionId_FinanceAcc~",
            schema: "sirkadiyen",
            table: "profit_distribution_shares",
            columns: new[] { "FinanceDistributionId", "FinanceAccountHolderId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_profit_distribution_shares_FinanceTransactionId",
            schema: "sirkadiyen",
            table: "profit_distribution_shares",
            column: "FinanceTransactionId",
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_finance_transactions_finance_distributions_FinanceDistribut~",
            schema: "sirkadiyen",
            table: "finance_transactions",
            column: "FinanceDistributionId",
            principalSchema: "sirkadiyen",
            principalTable: "finance_distributions",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_finance_transactions_finance_distributions_FinanceDistribut~",
            schema: "sirkadiyen",
            table: "finance_transactions");

        migrationBuilder.DropTable(
            name: "profit_distribution_shares",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "finance_distributions",
            schema: "sirkadiyen");
    }
}
