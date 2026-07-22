using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <summary>
/// Adds the explicitly sourced academic department used by semantic secondary
/// matching. Existing rows remain null and are never inferred.
/// </summary>
[DbContext(typeof(SirkadiyenDbContext))]
[Migration("20260722180000_AddCanonicalDepartment")]
public sealed class AddCanonicalDepartment : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Department",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "character varying(500)",
            maxLength: 500,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Department",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");
    }
}
