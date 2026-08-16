using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RecurringTaskRules",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MessageTemplate = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    OffsetMinutes = table.Column<int>(type: "integer", nullable: true),
                    CooldownDays = table.Column<int>(type: "integer", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTaskRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecurringTaskExecutions",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    RuleId = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RecipientType = table.Column<int>(type: "integer", nullable: false),
                    ClientId = table.Column<byte[]>(type: "bytea", nullable: true),
                    TeacherId = table.Column<byte[]>(type: "bytea", nullable: true),
                    AppointmentId = table.Column<byte[]>(type: "bytea", nullable: true),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    DeduplicationKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    GeneratedText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CompletedByUserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    SkippedByUserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SkippedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringTaskExecutions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecurringTaskExecutions_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecurringTaskExecutions_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecurringTaskExecutions_RecurringTaskRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "RecurringTaskRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecurringTaskExecutions_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecurringTaskExecutions_Users_SkippedByUserId",
                        column: x => x.SkippedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RecurringTaskExecutions_Users_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "RecurringTaskRules",
                columns: new[] { "Id", "CooldownDays", "CreatedAtUtc", "IsEnabled", "MessageTemplate", "Name", "OffsetMinutes", "Type", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new byte[] { 1, 151, 42, 51, 124, 145, 248, 218, 56, 50, 247, 8, 50, 250, 214, 128 }, null, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Client.FirstName}! Напоминаем, что {When} в {Appointment.StartTime} у вас запланировано занятие.", "Напоминание о записи", 1440, 0, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new byte[] { 1, 151, 42, 51, 176, 117, 100, 5, 171, 23, 40, 76, 250, 42, 17, 23 }, 365, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Client.FirstName}! Поздравляем вас с днём рождения! Желаем хорошего дня, отличного настроения и вдохновения.", "Поздравление с днём рождения", null, 1, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new byte[] { 1, 151, 42, 51, 212, 133, 109, 202, 202, 239, 232, 93, 167, 158, 71, 0 }, null, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Client.FirstName}! Спасибо, что пришли на пробное занятие. Хотите подобрать удобное время для следующих занятий?", "Связаться после пробного занятия", 1440, 2, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new byte[] { 1, 151, 42, 52, 7, 26, 3, 211, 249, 95, 79, 189, 125, 40, 160, 10 }, 7, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Client.FirstName}! Вы давно не были на занятиях. Хотите подобрать удобное время для следующего занятия?", "Напомнить о занятиях", null, 3, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExecutions_AppointmentId",
                table: "RecurringTaskExecutions",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExecutions_ClientId_RuleId_CompletedAtUtc",
                table: "RecurringTaskExecutions",
                columns: new[] { "ClientId", "RuleId", "CompletedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExecutions_CompletedByUserId",
                table: "RecurringTaskExecutions",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExecutions_DeduplicationKey",
                table: "RecurringTaskExecutions",
                column: "DeduplicationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExecutions_RuleId_Status",
                table: "RecurringTaskExecutions",
                columns: new[] { "RuleId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExecutions_SkippedByUserId",
                table: "RecurringTaskExecutions",
                column: "SkippedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExecutions_TeacherId",
                table: "RecurringTaskExecutions",
                column: "TeacherId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecurringTaskExecutions");

            migrationBuilder.DropTable(
                name: "RecurringTaskRules");
        }
    }
}
