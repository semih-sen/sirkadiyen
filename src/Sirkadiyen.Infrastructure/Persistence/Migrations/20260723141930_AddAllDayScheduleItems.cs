using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sirkadiyen.Infrastructure.Persistence.Migrations;

/// <summary>
/// Lets a canonical record be all-day instead of timed (ADR-046).
/// </summary>
/// <remarks>
/// <para>
/// Forward is additive and safe. Every existing record is timed, so
/// <c>IsAllDay</c> defaults to false, the two time columns keep their values, and
/// the new shape constraint holds for every row already stored. The old
/// time-order constraint is replaced rather than kept, because the rule is now
/// "either timed with ordered times or all-day with none" and the two halves must
/// be checked together.
/// </para>
/// <para>
/// Backward refuses when all-day records exist, deliberately. The previous schema
/// has no way to store a holiday: making the time columns required again would
/// have to invent midnight for it, which would publish a 00:00 event, and
/// deleting the rows would silently discard published schedule data that a diff
/// may already cite. Both are worse than a failure that names the problem, so the
/// guard raises a readable exception and the rollback decision is made by a
/// person.
/// </para>
/// </remarks>
public partial class AddAllDayScheduleItems : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_canonical_schedule_records_time_order",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");

        migrationBuilder.AlterColumn<TimeOnly>(
            name: "StartLocalTime",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "time without time zone",
            nullable: true,
            oldClrType: typeof(TimeOnly),
            oldType: "time without time zone");

        migrationBuilder.AlterColumn<TimeOnly>(
            name: "EndLocalTime",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "time without time zone",
            nullable: true,
            oldClrType: typeof(TimeOnly),
            oldType: "time without time zone");

        migrationBuilder.AddColumn<bool>(
            name: "IsAllDay",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddCheckConstraint(
            name: "ck_canonical_schedule_records_schedule_shape",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            sql: """
                ("IsAllDay" AND "StartLocalTime" IS NULL AND "EndLocalTime" IS NULL)
                OR (NOT "IsAllDay"
                    AND "StartLocalTime" IS NOT NULL
                    AND "EndLocalTime" IS NOT NULL
                    AND "EndLocalTime" > "StartLocalTime")
                """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        // Refuse before altering anything, so a rollback that cannot preserve the
        // data stops with an explanation instead of a constraint violation.
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                all_day_count bigint;
            BEGIN
                SELECT count(*) INTO all_day_count
                FROM sirkadiyen.canonical_schedule_records
                WHERE "IsAllDay";

                IF all_day_count > 0 THEN
                    RAISE EXCEPTION
                        'Cannot roll back AddAllDayScheduleItems: % all-day record(s) exist '
                        'and the previous schema cannot represent them. Decide what happens '
                        'to those records before reverting.', all_day_count;
                END IF;
            END $$;
            """);

        migrationBuilder.DropCheckConstraint(
            name: "ck_canonical_schedule_records_schedule_shape",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");

        migrationBuilder.DropColumn(
            name: "IsAllDay",
            schema: "sirkadiyen",
            table: "canonical_schedule_records");

        migrationBuilder.AlterColumn<TimeOnly>(
            name: "StartLocalTime",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "time without time zone",
            nullable: false,
            oldClrType: typeof(TimeOnly),
            oldType: "time without time zone",
            oldNullable: true);

        migrationBuilder.AlterColumn<TimeOnly>(
            name: "EndLocalTime",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            type: "time without time zone",
            nullable: false,
            oldClrType: typeof(TimeOnly),
            oldType: "time without time zone",
            oldNullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_canonical_schedule_records_time_order",
            schema: "sirkadiyen",
            table: "canonical_schedule_records",
            sql: "\"EndLocalTime\" > \"StartLocalTime\"");
    }
}
