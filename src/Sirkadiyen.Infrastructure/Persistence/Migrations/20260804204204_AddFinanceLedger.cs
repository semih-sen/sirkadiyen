using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddFinanceLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "finance_account_holders",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: true),
                ShareBasisPoints = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_account_holders", x => x.Id);
                table.CheckConstraint("ck_finance_account_holders_inactive_has_no_share", "\"Status\" = 'Active' OR \"ShareBasisPoints\" = 0");
                table.CheckConstraint("ck_finance_account_holders_share", "\"ShareBasisPoints\" BETWEEN 0 AND 10000");
                table.CheckConstraint("ck_finance_account_holders_status", "\"Status\" IN ('Active', 'Inactive')");
                table.ForeignKey(
                    name: "FK_finance_account_holders_users_UserId",
                    column: x => x.UserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "finance_audits",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Sequence = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                SubjectType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                BeforeState = table.Column<string>(type: "jsonb", nullable: true),
                AfterState = table.Column<string>(type: "jsonb", nullable: true),
                ChangedFields = table.Column<string>(type: "jsonb", nullable: false),
                AmountDelta = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_audits", x => x.Id);
                table.CheckConstraint("ck_finance_audits_action", "\"Action\" IN ('AccountOpened', 'AccountUpdated', 'AccountClosed', 'HolderCreated',\n             'HolderUpdated', 'HolderDeactivated', 'PartnerSharesChanged',\n             'TransactionCreated', 'TransactionUpdated', 'TransactionDeleted',\n             'ObligationCreated', 'ObligationUpdated', 'ObligationSettled',\n             'ObligationSettlementCancelled', 'ObligationWrittenOff', 'ObligationCancelled',\n             'DistributionExecuted', 'DistributionReversed')");
                table.CheckConstraint("ck_finance_audits_reason_required", "\"Action\" NOT IN ('AccountClosed', 'HolderDeactivated', 'TransactionUpdated', 'TransactionDeleted', 'ObligationSettlementCancelled', 'ObligationWrittenOff', 'ObligationCancelled', 'DistributionExecuted', 'DistributionReversed') OR \"Reason\" IS NOT NULL");
                table.ForeignKey(
                    name: "FK_finance_audits_users_ActorUserId",
                    column: x => x.ActorUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "finance_transactions",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false),
                Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                CounterpartyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                FinanceDistributionId = table.Column<Guid>(type: "uuid", nullable: true),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                UpdatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_transactions", x => x.Id);
                table.CheckConstraint("ck_finance_transactions_amount", "\"Amount\" > 0");
                table.CheckConstraint("ck_finance_transactions_category", "(\"Kind\" = 'Income'  AND \"Category\" IN ('LicenseSales', 'Sponsorship', 'Donation', 'OtherIncome'))\nOR (\"Kind\" = 'Expense' AND \"Category\" IN ('Servers', 'Domains', 'ExternalServices',\n                                'SoftwareLicenses', 'Marketing', 'Operational', 'Charitable', 'OtherExpense'))\nOR (\"Kind\" IN ('OpeningBalance', 'Transfer', 'Distribution') AND \"Category\" IS NULL)");
                table.CheckConstraint("ck_finance_transactions_distribution_link", "(\"Kind\" = 'Distribution') = (\"FinanceDistributionId\" IS NOT NULL)");
                table.CheckConstraint("ck_finance_transactions_kind", "\"Kind\" IN ('OpeningBalance', 'Income', 'Expense', 'Transfer', 'Distribution')");
                table.CheckConstraint("ck_finance_transactions_revision", "\"RevisionNumber\" >= 1");
                table.ForeignKey(
                    name: "FK_finance_transactions_users_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_finance_transactions_users_UpdatedByUserId",
                    column: x => x.UpdatedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "finance_accounts",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FinanceAccountHolderId = table.Column<Guid>(type: "uuid", nullable: false),
                Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CurrencyCode = table.Column<string>(type: "char(3)", nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                OpenedOn = table.Column<DateOnly>(type: "date", nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ClosedReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_accounts", x => x.Id);
                table.CheckConstraint("ck_finance_accounts_closure", "(\"Status\" = 'Closed' AND \"ClosedAtUtc\" IS NOT NULL AND \"ClosedReason\" IS NOT NULL)\nOR\n(\"Status\" <> 'Closed' AND \"ClosedAtUtc\" IS NULL AND \"ClosedReason\" IS NULL)");
                table.CheckConstraint("ck_finance_accounts_currency", "\"CurrencyCode\" = 'TRY'");
                table.CheckConstraint("ck_finance_accounts_kind", "\"Kind\" IN ('Cash', 'Bank')");
                table.CheckConstraint("ck_finance_accounts_status", "\"Status\" IN ('Active', 'Closed')");
                table.ForeignKey(
                    name: "FK_finance_accounts_finance_account_holders_FinanceAccountHold~",
                    column: x => x.FinanceAccountHolderId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_account_holders",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "finance_ledger_entries",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FinanceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                FinanceAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                Leg = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                OccurredOn = table.Column<DateOnly>(type: "date", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_finance_ledger_entries", x => x.Id);
                table.CheckConstraint("ck_finance_ledger_entries_amount", "\"Amount\" <> 0");
                table.CheckConstraint("ck_finance_ledger_entries_kind", "\"Kind\" IN ('OpeningBalance', 'Income', 'Expense', 'Transfer', 'Distribution')");
                table.CheckConstraint("ck_finance_ledger_entries_leg", "(\"Kind\" = 'Transfer' AND \"Leg\" IN ('From', 'To') AND ((\"Leg\" = 'From') = (\"Amount\" < 0)))\nOR (\"Kind\" IN ('OpeningBalance', 'Income', 'Expense', 'Distribution') AND \"Leg\" = 'Single')");
                table.ForeignKey(
                    name: "FK_finance_ledger_entries_finance_accounts_FinanceAccountId",
                    column: x => x.FinanceAccountId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_accounts",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_finance_ledger_entries_finance_transactions_FinanceTransact~",
                    column: x => x.FinanceTransactionId,
                    principalSchema: "sirkadiyen",
                    principalTable: "finance_transactions",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_finance_account_holders_DisplayName",
            schema: "sirkadiyen",
            table: "finance_account_holders",
            column: "DisplayName",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_account_holders_UserId",
            schema: "sirkadiyen",
            table: "finance_account_holders",
            column: "UserId",
            unique: true,
            filter: "\"UserId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_finance_accounts_FinanceAccountHolderId_Name",
            schema: "sirkadiyen",
            table: "finance_accounts",
            columns: new[] { "FinanceAccountHolderId", "Name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_audits_Action_OccurredAtUtc",
            schema: "sirkadiyen",
            table: "finance_audits",
            columns: new[] { "Action", "OccurredAtUtc" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_audits_ActorUserId",
            schema: "sirkadiyen",
            table: "finance_audits",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_finance_audits_OccurredAtUtc",
            schema: "sirkadiyen",
            table: "finance_audits",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_finance_audits_SubjectType_SubjectId_Sequence",
            schema: "sirkadiyen",
            table: "finance_audits",
            columns: new[] { "SubjectType", "SubjectId", "Sequence" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_ledger_entries_FinanceAccountId_Kind",
            schema: "sirkadiyen",
            table: "finance_ledger_entries",
            columns: new[] { "FinanceAccountId", "Kind" },
            unique: true,
            filter: "\"Kind\" = 'OpeningBalance'");

        migrationBuilder.CreateIndex(
            name: "IX_finance_ledger_entries_FinanceAccountId_OccurredOn",
            schema: "sirkadiyen",
            table: "finance_ledger_entries",
            columns: new[] { "FinanceAccountId", "OccurredOn" })
            .Annotation("Npgsql:IndexInclude", new[] { "Amount" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_ledger_entries_FinanceTransactionId_FinanceAccountId",
            schema: "sirkadiyen",
            table: "finance_ledger_entries",
            columns: new[] { "FinanceTransactionId", "FinanceAccountId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_ledger_entries_FinanceTransactionId_Leg",
            schema: "sirkadiyen",
            table: "finance_ledger_entries",
            columns: new[] { "FinanceTransactionId", "Leg" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_finance_ledger_entries_OccurredOn_Kind",
            schema: "sirkadiyen",
            table: "finance_ledger_entries",
            columns: new[] { "OccurredOn", "Kind" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_transactions_Category_OccurredOn",
            schema: "sirkadiyen",
            table: "finance_transactions",
            columns: new[] { "Category", "OccurredOn" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_transactions_CreatedByUserId",
            schema: "sirkadiyen",
            table: "finance_transactions",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_finance_transactions_FinanceDistributionId",
            schema: "sirkadiyen",
            table: "finance_transactions",
            column: "FinanceDistributionId");

        migrationBuilder.CreateIndex(
            name: "IX_finance_transactions_Kind_OccurredOn",
            schema: "sirkadiyen",
            table: "finance_transactions",
            columns: new[] { "Kind", "OccurredOn" });

        migrationBuilder.CreateIndex(
            name: "IX_finance_transactions_OccurredOn",
            schema: "sirkadiyen",
            table: "finance_transactions",
            column: "OccurredOn");

        migrationBuilder.CreateIndex(
            name: "IX_finance_transactions_UpdatedByUserId",
            schema: "sirkadiyen",
            table: "finance_transactions",
            column: "UpdatedByUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "finance_audits",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "finance_ledger_entries",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "finance_accounts",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "finance_transactions",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "finance_account_holders",
            schema: "sirkadiyen");
    }
}
