using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSingleUseLicensing : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "licenses",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CodeHash = table.Column<byte[]>(type: "bytea", nullable: false),
                Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ExpiresAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RedeemedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                RedeemedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                RevokedByEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                RevocationReason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                RevokedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_licenses", x => x.Id);
                table.CheckConstraint("ck_licenses_code_hash_length", "octet_length(\"CodeHash\") = 32");
                table.CheckConstraint("ck_licenses_expiration", "\"ExpiresAtUtc\" IS NULL OR \"ExpiresAtUtc\" > \"CreatedAtUtc\"");
                table.CheckConstraint("ck_licenses_redemption", "(\"RedeemedByUserId\" IS NULL) = (\"RedeemedAtUtc\" IS NULL)\nAND (\"Status\" <> 'Redeemed'\n     OR (\"RedeemedByUserId\" IS NOT NULL AND \"RedeemedAtUtc\" IS NOT NULL))\nAND (\"Status\" NOT IN ('Active', 'Expired')\n     OR \"RedeemedByUserId\" IS NULL)");
                table.CheckConstraint("ck_licenses_revocation", "(\"Status\" = 'Revoked'\n AND \"RevokedByUserId\" IS NOT NULL\n AND \"RevokedByEmail\" IS NOT NULL\n AND \"RevocationReason\" IS NOT NULL\n AND \"RevokedAtUtc\" IS NOT NULL)\nOR\n(\"Status\" <> 'Revoked'\n AND \"RevokedByUserId\" IS NULL\n AND \"RevokedByEmail\" IS NULL\n AND \"RevocationReason\" IS NULL\n AND \"RevokedAtUtc\" IS NULL)");
                table.CheckConstraint("ck_licenses_status", "\"Status\" IN ('Active', 'Redeemed', 'Revoked', 'Expired')");
                table.ForeignKey(
                    name: "FK_licenses_users_CreatedByUserId",
                    column: x => x.CreatedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_licenses_users_RedeemedByUserId",
                    column: x => x.RedeemedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_licenses_users_RevokedByUserId",
                    column: x => x.RevokedByUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "license_audits",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_license_audits", x => x.Id);
                table.CheckConstraint("ck_license_audits_action", "\"Action\" IN ('Created', 'Redeemed', 'Revoked', 'Expired')");
                table.ForeignKey(
                    name: "FK_license_audits_licenses_LicenseId",
                    column: x => x.LicenseId,
                    principalSchema: "sirkadiyen",
                    principalTable: "licenses",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_license_audits_users_ActorUserId",
                    column: x => x.ActorUserId,
                    principalSchema: "sirkadiyen",
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_license_audits_ActorUserId",
            schema: "sirkadiyen",
            table: "license_audits",
            column: "ActorUserId");

        migrationBuilder.CreateIndex(
            name: "IX_license_audits_LicenseId",
            schema: "sirkadiyen",
            table: "license_audits",
            column: "LicenseId");

        migrationBuilder.CreateIndex(
            name: "IX_license_audits_OccurredAtUtc",
            schema: "sirkadiyen",
            table: "license_audits",
            column: "OccurredAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_licenses_CodeHash",
            schema: "sirkadiyen",
            table: "licenses",
            column: "CodeHash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_licenses_CreatedByUserId",
            schema: "sirkadiyen",
            table: "licenses",
            column: "CreatedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_licenses_ExpiresAtUtc",
            schema: "sirkadiyen",
            table: "licenses",
            column: "ExpiresAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_licenses_RedeemedByUserId",
            schema: "sirkadiyen",
            table: "licenses",
            column: "RedeemedByUserId",
            unique: true,
            filter: "\"Status\" = 'Redeemed'");

        migrationBuilder.CreateIndex(
            name: "IX_licenses_RevokedByUserId",
            schema: "sirkadiyen",
            table: "licenses",
            column: "RevokedByUserId");

        migrationBuilder.CreateIndex(
            name: "IX_licenses_Status",
            schema: "sirkadiyen",
            table: "licenses",
            column: "Status");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "license_audits",
            schema: "sirkadiyen");

        migrationBuilder.DropTable(
            name: "licenses",
            schema: "sirkadiyen");
    }
}
