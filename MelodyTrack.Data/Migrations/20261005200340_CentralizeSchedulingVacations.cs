using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class CentralizeSchedulingVacations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_RecurrenceRules_RecurringRuleId",
                table: "Appointments");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_RecurringRuleId",
                table: "Appointments");

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "WorkingHoursRequests",
                type: "text",
                nullable: false,
                defaultValue: "Europe/Moscow");

            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "VacationRequests",
                type: "text",
                nullable: false,
                defaultValue: "Create");

            migrationBuilder.AddColumn<int>(
                name: "ExpectedVacationVersion",
                table: "VacationRequests",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "TargetVacationId",
                table: "VacationRequests",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleTimezone",
                table: "Users",
                type: "text",
                nullable: false,
                defaultValue: "Europe/Moscow");

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "RecurrenceRules",
                type: "integer",
                nullable: false,
                defaultValue: 60);

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "RecurrenceRules",
                type: "text",
                nullable: false,
                defaultValue: "UTC");

            migrationBuilder.AddColumn<long>(
                name: "RecurrenceOrdinal",
                table: "Appointments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Vacations",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    UserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    ClientId = table.Column<byte[]>(type: "bytea", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OriginalEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsCancelled = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vacations", x => x.Id);
                    table.CheckConstraint("CK_Vacations_Range", "\"StartDate\" < \"EndDate\"");
                    table.CheckConstraint("CK_Vacations_Subject", "(\"UserId\" IS NULL) <> (\"ClientId\" IS NULL)");
                    table.CheckConstraint("CK_Vacations_Version", "\"Version\" > 0");
                    table.ForeignKey(
                        name: "FK_Vacations_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Vacations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SchedulingInterruptions",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    VacationId = table.Column<byte[]>(type: "bytea", nullable: true),
                    SubjectType = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<byte[]>(type: "bytea", nullable: false),
                    AppointmentId = table.Column<byte[]>(type: "bytea", nullable: true),
                    RecurringRuleId = table.Column<byte[]>(type: "bytea", nullable: true),
                    RecurrenceOrdinal = table.Column<long>(type: "bigint", nullable: true),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: false),
                    SupersededAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RestoredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchedulingInterruptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SchedulingInterruptions_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SchedulingInterruptions_RecurrenceRules_RecurringRuleId",
                        column: x => x.RecurringRuleId,
                        principalTable: "RecurrenceRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SchedulingInterruptions_Vacations_VacationId",
                        column: x => x.VacationId,
                        principalTable: "Vacations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve legacy identities before removing the two owner-specific tables.
            // Empty legacy intervals are retained as cancelled records with their original end.
            migrationBuilder.Sql("""
                INSERT INTO "Vacations" ("Id", "UserId", "ClientId", "StartDate", "EndDate",
                    "OriginalEndDate", "IsCancelled", "Version")
                SELECT "Id", NULL, "ClientId", "StartDate",
                    CASE WHEN "EndDate" > "StartDate" THEN "EndDate" ELSE "StartDate" + interval '1 microsecond' END,
                    CASE WHEN "EndDate" <= "StartDate" THEN "EndDate" END, "EndDate" <= "StartDate", 1
                FROM "ClientVacations"
                UNION ALL
                SELECT "Id", "UserId", NULL, "StartDate",
                    CASE WHEN "EndDate" > "StartDate" THEN "EndDate" ELSE "StartDate" + interval '1 microsecond' END,
                    CASE WHEN "EndDate" <= "StartDate" THEN "EndDate" END, "EndDate" <= "StartDate", 1
                FROM "UserVacations";

                -- Old bulk replacement could leave an approved request pointing at a removed ID.
                UPDATE "VacationRequests" q SET "ResultingVacationId" = (
                    SELECT v."Id" FROM "Vacations" v
                    WHERE ((q."SubjectType" = 'Client' AND v."ClientId" = q."SubjectId")
                        OR (q."SubjectType" = 'Staff' AND v."UserId" = q."SubjectId"))
                        AND v."StartDate" = q."RequestedStart" AND v."EndDate" = q."RequestedEnd"
                    ORDER BY v."Id" LIMIT 1)
                WHERE q."ResultingVacationId" IS NOT NULL
                    AND NOT EXISTS (SELECT 1 FROM "Vacations" v WHERE v."Id" = q."ResultingVacationId");

                UPDATE "Appointments" a SET "RecurrenceOrdinal" = CASE WHEN t."Type" = 2
                    THEN ((extract(year FROM a."StartDate" AT TIME ZONE 'UTC')
                         - extract(year FROM r."StartDate" AT TIME ZONE 'UTC')) * 12
                         + extract(month FROM a."StartDate" AT TIME ZONE 'UTC')
                         - extract(month FROM r."StartDate" AT TIME ZONE 'UTC'))::bigint
                    ELSE ((a."StartDate" AT TIME ZONE 'UTC')::date
                         - (r."StartDate" AT TIME ZONE 'UTC')::date)::bigint END
                FROM "RecurrenceRules" r JOIN "RecurrenceTypes" t ON t."Id" = r."RecurrenceTypeId"
                WHERE a."RecurringRuleId" = r."Id";

                -- Preserve duplicate legacy rows as standalone appointments, never delete history.
                WITH duplicates AS (
                    SELECT "Id", row_number() OVER (PARTITION BY "RecurringRuleId", "RecurrenceOrdinal"
                        ORDER BY "IsDeleted", CASE "Status" WHEN 1 THEN 0 WHEN 3 THEN 0 WHEN 0 THEN 1 ELSE 2 END, "Id") AS position
                    FROM "Appointments" WHERE "RecurringRuleId" IS NOT NULL AND "RecurrenceOrdinal" IS NOT NULL)
                UPDATE "Appointments" a SET "RecurringRuleId" = NULL, "RecurrenceOrdinal" = NULL
                FROM duplicates d WHERE d."Id" = a."Id" AND d.position > 1;

                -- Record only cancellations that this migration actually performs. Already-cancelled
                -- rows have no reliable legacy reason and must not become restoration candidates.
                INSERT INTO "SchedulingInterruptions" ("Id", "VacationId", "SubjectType", "SubjectId",
                    "AppointmentId", "RecurringRuleId", "RecurrenceOrdinal", "StartDate", "EndDate", "Reason")
                SELECT decode(lpad(to_hex((extract(epoch FROM current_timestamp) * 1000)::bigint), 12, '0')
                    || substr(encode(uuid_send(gen_random_uuid()), 'hex'), 13), 'hex'),
                    v."Id", CASE WHEN v."ClientId" IS NULL THEN 'Staff' ELSE 'Client' END,
                    coalesce(v."ClientId", v."UserId"), a."Id", a."RecurringRuleId", a."RecurrenceOrdinal",
                    a."StartDate", a."EndDate", 'vacation'
                FROM "Appointments" a JOIN "Vacations" v ON NOT v."IsCancelled"
                    AND (v."ClientId" = a."ClientId" OR v."UserId" = a."ProviderId")
                    AND v."StartDate" < a."EndDate" AND a."StartDate" < v."EndDate"
                WHERE a."Status" = 0 AND NOT a."IsDeleted";

                UPDATE "Appointments" a SET "Status" = 2
                WHERE EXISTS (SELECT 1 FROM "SchedulingInterruptions" i WHERE i."AppointmentId" = a."Id");

                -- Remember future unmaterialized occurrences across the entire vacation, including
                -- weeks that have never been opened. Legacy recurrence rules used UTC calendar days.
                INSERT INTO "SchedulingInterruptions" ("Id", "VacationId", "SubjectType", "SubjectId",
                    "RecurringRuleId", "RecurrenceOrdinal", "StartDate", "EndDate", "Reason")
                SELECT decode(lpad(to_hex((extract(epoch FROM current_timestamp) * 1000)::bigint), 12, '0')
                    || substr(encode(uuid_send(gen_random_uuid()), 'hex'), 13), 'hex'),
                    v."Id", CASE WHEN v."ClientId" IS NULL THEN 'Staff' ELSE 'Client' END,
                    coalesce(v."ClientId", v."UserId"), r."Id", occurrence.ordinal,
                    occurrence.start_at, occurrence.start_at + make_interval(mins => r."DurationMinutes"), 'vacation'
                FROM "Vacations" v JOIN "RecurrenceRules" r
                    ON (v."ClientId" = r."ClientId" OR v."UserId" = r."ProviderId")
                JOIN "RecurrenceTypes" t ON t."Id" = r."RecurrenceTypeId"
                CROSS JOIN LATERAL generate_series(
                    greatest((r."StartDate" AT TIME ZONE 'UTC')::date,
                        ((v."StartDate" - make_interval(mins => r."DurationMinutes")) AT TIME ZONE 'UTC')::date)::timestamp,
                    least((v."EndDate" AT TIME ZONE 'UTC')::date,
                        coalesce((r."EndDate" AT TIME ZONE 'UTC')::date, (v."EndDate" AT TIME ZONE 'UTC')::date))::timestamp,
                    interval '1 day') AS days(local_day)
                CROSS JOIN LATERAL (SELECT
                    (days.local_day::date + (r."StartDate" AT TIME ZONE 'UTC')::time) AT TIME ZONE 'UTC' AS start_at,
                    CASE WHEN t."Type" = 2 THEN
                        ((extract(year FROM days.local_day) - extract(year FROM r."StartDate" AT TIME ZONE 'UTC')) * 12
                         + extract(month FROM days.local_day) - extract(month FROM r."StartDate" AT TIME ZONE 'UTC'))::bigint
                        ELSE (days.local_day::date - (r."StartDate" AT TIME ZONE 'UTC')::date)::bigint END AS ordinal) occurrence
                WHERE NOT v."IsCancelled" AND occurrence.start_at > current_timestamp
                    AND occurrence.start_at >= r."StartDate"
                    AND (r."EndDate" IS NULL OR occurrence.start_at <= r."EndDate")
                    AND occurrence.start_at < v."EndDate"
                    AND v."StartDate" < occurrence.start_at + make_interval(mins => r."DurationMinutes")
                    AND (t."Type" = 0
                        OR (t."Type" = 1 AND (r."RecurrencePattern" & (1 << (extract(isodow FROM days.local_day)::int - 1))) <> 0)
                        OR (t."Type" = 2 AND extract(day FROM days.local_day)::int = r."RecurrencePattern"))
                    AND NOT EXISTS (SELECT 1 FROM "Appointments" a
                        WHERE a."RecurringRuleId" = r."Id" AND a."RecurrenceOrdinal" = occurrence.ordinal);
                """);

            migrationBuilder.DropTable(name: "ClientVacations");
            migrationBuilder.DropTable(name: "UserVacations");

            migrationBuilder.CreateIndex(
                name: "IX_VacationRequests_ResultingVacationId",
                table: "VacationRequests",
                column: "ResultingVacationId");

            migrationBuilder.CreateIndex(
                name: "IX_VacationRequests_TargetVacationId",
                table: "VacationRequests",
                column: "TargetVacationId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RecurringRuleId_RecurrenceOrdinal",
                table: "Appointments",
                columns: new[] { "RecurringRuleId", "RecurrenceOrdinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingInterruptions_AppointmentId",
                table: "SchedulingInterruptions",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingInterruptions_RecurringRuleId_RecurrenceOrdinal",
                table: "SchedulingInterruptions",
                columns: new[] { "RecurringRuleId", "RecurrenceOrdinal" });

            migrationBuilder.CreateIndex(
                name: "IX_SchedulingInterruptions_VacationId_RestoredAtUtc",
                table: "SchedulingInterruptions",
                columns: new[] { "VacationId", "RestoredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Vacations_ClientId_StartDate",
                table: "Vacations",
                columns: new[] { "ClientId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Vacations_UserId_StartDate",
                table: "Vacations",
                columns: new[] { "UserId", "StartDate" });

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_RecurrenceRules_RecurringRuleId",
                table: "Appointments",
                column: "RecurringRuleId",
                principalTable: "RecurrenceRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VacationRequests_Vacations_ResultingVacationId",
                table: "VacationRequests",
                column: "ResultingVacationId",
                principalTable: "Vacations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_VacationRequests_Vacations_TargetVacationId",
                table: "VacationRequests",
                column: "TargetVacationId",
                principalTable: "Vacations",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A UTC-only predecessor cannot represent newly created local-time recurrence rules.
            // Refuse a silent schedule shift; an explicit conversion is required before rollback.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "RecurrenceRules" WHERE "Timezone" <> 'UTC') THEN
                        RAISE EXCEPTION 'Convert local-time recurrence rules to UTC before rolling back centralized scheduling.';
                    END IF;
                END $$;
                UPDATE "VacationRequests" SET "Status" = 'Cancelled', "ProcessedAtUtc" = current_timestamp,
                    "Version" = "Version" + 1 WHERE "Action" <> 'Create' AND "Status" = 'Pending';
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_RecurrenceRules_RecurringRuleId",
                table: "Appointments");

            migrationBuilder.DropForeignKey(
                name: "FK_VacationRequests_Vacations_ResultingVacationId",
                table: "VacationRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_VacationRequests_Vacations_TargetVacationId",
                table: "VacationRequests");

            migrationBuilder.DropTable(
                name: "SchedulingInterruptions");

            migrationBuilder.DropIndex(
                name: "IX_VacationRequests_ResultingVacationId",
                table: "VacationRequests");

            migrationBuilder.DropIndex(
                name: "IX_VacationRequests_TargetVacationId",
                table: "VacationRequests");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_RecurringRuleId_RecurrenceOrdinal",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "WorkingHoursRequests");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "VacationRequests");

            migrationBuilder.DropColumn(
                name: "ExpectedVacationVersion",
                table: "VacationRequests");

            migrationBuilder.DropColumn(
                name: "TargetVacationId",
                table: "VacationRequests");

            migrationBuilder.DropColumn(
                name: "ScheduleTimezone",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "RecurrenceRules");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "RecurrenceRules");

            migrationBuilder.DropColumn(
                name: "RecurrenceOrdinal",
                table: "Appointments");

            migrationBuilder.CreateTable(
                name: "ClientVacations",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    ClientId = table.Column<byte[]>(type: "bytea", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientVacations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientVacations_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserVacations",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    UserId = table.Column<byte[]>(type: "bytea", nullable: false),
                    EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserVacations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserVacations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql("""
                INSERT INTO "ClientVacations" ("Id", "ClientId", "StartDate", "EndDate")
                SELECT "Id", "ClientId", "StartDate", "EndDate" FROM "Vacations"
                WHERE "ClientId" IS NOT NULL AND NOT "IsCancelled";
                INSERT INTO "UserVacations" ("Id", "UserId", "StartDate", "EndDate")
                SELECT "Id", "UserId", "StartDate", "EndDate" FROM "Vacations"
                WHERE "UserId" IS NOT NULL AND NOT "IsCancelled";
                """);
            migrationBuilder.DropTable(name: "Vacations");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_RecurringRuleId",
                table: "Appointments",
                column: "RecurringRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientVacations_ClientId",
                table: "ClientVacations",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_UserVacations_UserId",
                table: "UserVacations",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_RecurrenceRules_RecurringRuleId",
                table: "Appointments",
                column: "RecurringRuleId",
                principalTable: "RecurrenceRules",
                principalColumn: "Id");
        }
    }
}
