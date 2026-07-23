using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddManualLicenseActivation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_licenses_code_hash_length",
            schema: "sirkadiyen",
            table: "licenses");

        migrationBuilder.DropCheckConstraint(
            name: "ck_license_audits_action",
            schema: "sirkadiyen",
            table: "license_audits");

        migrationBuilder.AlterColumn<byte[]>(
            name: "CodeHash",
            schema: "sirkadiyen",
            table: "licenses",
            type: "bytea",
            nullable: true,
            oldClrType: typeof(byte[]),
            oldType: "bytea");

        migrationBuilder.AddColumn<string>(
            name: "Kind",
            schema: "sirkadiyen",
            table: "licenses",
            type: "character varying(40)",
            maxLength: 40,
            nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE sirkadiyen.licenses
            SET "Kind" = 'Code'
            WHERE "Kind" IS NULL
            """);

        migrationBuilder.AlterColumn<string>(
            name: "Kind",
            schema: "sirkadiyen",
            table: "licenses",
            type: "character varying(40)",
            maxLength: 40,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(40)",
            oldMaxLength: 40,
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_licenses_code_hash",
            schema: "sirkadiyen",
            table: "licenses",
            sql: "(\"Kind\" = 'Code' AND octet_length(\"CodeHash\") = 32)\nOR (\"Kind\" = 'Manual' AND \"CodeHash\" IS NULL)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_licenses_kind",
            schema: "sirkadiyen",
            table: "licenses",
            sql: "\"Kind\" IN ('Code', 'Manual')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_license_audits_action",
            schema: "sirkadiyen",
            table: "license_audits",
            sql: "\"Action\" IN ('Created', 'Redeemed', 'ManuallyActivated', 'Revoked', 'Expired')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT 1
                    FROM sirkadiyen.licenses
                    WHERE "Kind" = 'Manual'
                ) THEN
                    RAISE EXCEPTION
                        'Cannot roll back manual license activation while Manual licenses exist.';
                END IF;
            END $$;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "ck_licenses_code_hash",
            schema: "sirkadiyen",
            table: "licenses");

        migrationBuilder.DropCheckConstraint(
            name: "ck_licenses_kind",
            schema: "sirkadiyen",
            table: "licenses");

        migrationBuilder.DropCheckConstraint(
            name: "ck_license_audits_action",
            schema: "sirkadiyen",
            table: "license_audits");

        migrationBuilder.DropColumn(
            name: "Kind",
            schema: "sirkadiyen",
            table: "licenses");

        migrationBuilder.AlterColumn<byte[]>(
            name: "CodeHash",
            schema: "sirkadiyen",
            table: "licenses",
            type: "bytea",
            nullable: false,
            oldClrType: typeof(byte[]),
            oldType: "bytea",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_licenses_code_hash_length",
            schema: "sirkadiyen",
            table: "licenses",
            sql: "octet_length(\"CodeHash\") = 32");

        migrationBuilder.AddCheckConstraint(
            name: "ck_license_audits_action",
            schema: "sirkadiyen",
            table: "license_audits",
            sql: "\"Action\" IN ('Created', 'Redeemed', 'Revoked', 'Expired')");
    }
}
