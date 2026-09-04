using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stage14TimedVacations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_VacationRequests_Range",
                table: "VacationRequests");

            migrationBuilder.Sql("""
                ALTER TABLE "VacationRequests"
                    ALTER COLUMN "RequestedStart" TYPE timestamp with time zone
                        USING ("RequestedStart"::timestamp AT TIME ZONE 'UTC'),
                    ALTER COLUMN "RequestedEnd" TYPE timestamp with time zone
                        USING (("RequestedEnd" + 1)::timestamp AT TIME ZONE 'UTC');
                ALTER TABLE "UserVacations"
                    ALTER COLUMN "StartDate" TYPE timestamp with time zone
                        USING ("StartDate"::timestamp AT TIME ZONE 'UTC'),
                    ALTER COLUMN "EndDate" TYPE timestamp with time zone
                        USING (("EndDate" + 1)::timestamp AT TIME ZONE 'UTC');
                ALTER TABLE "ClientVacations"
                    ALTER COLUMN "StartDate" TYPE timestamp with time zone
                        USING ("StartDate"::timestamp AT TIME ZONE 'UTC'),
                    ALTER COLUMN "EndDate" TYPE timestamp with time zone
                        USING (("EndDate" + 1)::timestamp AT TIME ZONE 'UTC');
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_VacationRequests_Range",
                table: "VacationRequests",
                sql: "\"RequestedStart\" < \"RequestedEnd\"");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_VacationRequests_Range",
                table: "VacationRequests");

            migrationBuilder.Sql("""
                ALTER TABLE "VacationRequests"
                    ALTER COLUMN "RequestedStart" TYPE date
                        USING (("RequestedStart" AT TIME ZONE 'UTC')::date),
                    ALTER COLUMN "RequestedEnd" TYPE date
                        USING ((("RequestedEnd" AT TIME ZONE 'UTC') - interval '1 day')::date);
                ALTER TABLE "UserVacations"
                    ALTER COLUMN "StartDate" TYPE date
                        USING (("StartDate" AT TIME ZONE 'UTC')::date),
                    ALTER COLUMN "EndDate" TYPE date
                        USING ((("EndDate" AT TIME ZONE 'UTC') - interval '1 day')::date);
                ALTER TABLE "ClientVacations"
                    ALTER COLUMN "StartDate" TYPE date
                        USING (("StartDate" AT TIME ZONE 'UTC')::date),
                    ALTER COLUMN "EndDate" TYPE date
                        USING ((("EndDate" AT TIME ZONE 'UTC') - interval '1 day')::date);
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_VacationRequests_Range",
                table: "VacationRequests",
                sql: "\"RequestedStart\" <= \"RequestedEnd\"");
        }
    }
}
