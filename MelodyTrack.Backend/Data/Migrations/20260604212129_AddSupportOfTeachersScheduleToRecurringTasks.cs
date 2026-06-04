using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSupportOfTeachersScheduleToRecurringTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "RecurringTaskRules",
                columns: new[] { "Id", "CooldownDays", "CreatedAtUtc", "IsEnabled", "MessageTemplate", "Name", "OffsetMinutes", "Type", "UpdatedAtUtc" },
                values: new object[] { new byte[] { 1, 151, 42, 52, 58, 169, 122, 130, 189, 13, 60, 143, 67, 219, 74, 153 }, 1, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Teacher.FirstName}! Отправляем ваше расписание на {Date}.", "Отправить расписание преподавателю", null, 4, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RecurringTaskRules",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 151, 42, 52, 58, 169, 122, 130, 189, 13, 60, 143, 67, 219, 74, 153 });
        }
    }
}
