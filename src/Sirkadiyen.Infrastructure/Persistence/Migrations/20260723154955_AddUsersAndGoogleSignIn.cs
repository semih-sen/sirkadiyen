using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddUsersAndGoogleSignIn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            schema: "sirkadiyen",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                GoogleSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                NormalizedEmail = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                IsEmailVerified = table.Column<bool>(type: "boolean", nullable: false),
                DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                Role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LastSignedInAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_users", x => x.Id);
                table.CheckConstraint("ck_users_role", "\"Role\" IN ('User', 'SuperAdmin')");
                table.CheckConstraint("ck_users_verified_email", "\"IsEmailVerified\" = TRUE");
            });

        migrationBuilder.CreateIndex(
            name: "IX_users_GoogleSubject",
            schema: "sirkadiyen",
            table: "users",
            column: "GoogleSubject",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_users_LastSignedInAtUtc",
            schema: "sirkadiyen",
            table: "users",
            column: "LastSignedInAtUtc");

        migrationBuilder.CreateIndex(
            name: "IX_users_NormalizedEmail",
            schema: "sirkadiyen",
            table: "users",
            column: "NormalizedEmail",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "users",
            schema: "sirkadiyen");
    }
}
