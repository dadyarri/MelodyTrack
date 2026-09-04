using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDebtorRecurringTaskRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "RecurringTaskRules",
                columns: new[] { "Id", "CooldownDays", "CreatedAtUtc", "IsEnabled", "MessageTemplate", "Name", "OffsetMinutes", "Type", "UpdatedAtUtc" },
                values: new object[,]
                {
                    { new byte[] { 1, 151, 68, 39, 35, 32, 242, 46, 90, 222, 158, 36, 71, 1, 68, 170 }, null, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Client.FirstName}! Напоминаем, что у вас есть задолженность. Напишите нам, пожалуйста, если хотите уточнить сумму или подобрать удобный способ оплаты.", "Напомнить о долге через день", 1440, 5, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new byte[] { 1, 151, 68, 39, 35, 32, 242, 46, 90, 222, 158, 36, 71, 1, 68, 171 }, null, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Client.FirstName}! Напоминаем о задолженности по занятиям. Если удобно, можем помочь с оплатой или ответить на вопросы.", "Напомнить о долге через 3 дня", 4320, 5, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new byte[] { 1, 151, 68, 39, 35, 32, 242, 46, 90, 222, 158, 36, 71, 1, 68, 172 }, null, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Client.FirstName}! У вас по-прежнему есть задолженность. Напишите нам, если нужна помощь с оплатой или хотите обсудить детали.", "Напомнить о долге через неделю", 10080, 5, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new byte[] { 1, 151, 68, 39, 35, 32, 242, 46, 90, 222, 158, 36, 71, 1, 68, 173 }, 7, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc), true, "Здравствуйте, {Client.FirstName}! Напоминаем, что задолженность всё ещё не закрыта. Если нужна помощь или удобный вариант оплаты, мы на связи.", "Напоминать о долге каждую неделю", 10080, 5, new DateTime(2026, 6, 4, 0, 0, 0, 0, DateTimeKind.Utc) }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "RecurringTaskRules",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 151, 68, 39, 35, 32, 242, 46, 90, 222, 158, 36, 71, 1, 68, 170 });

            migrationBuilder.DeleteData(
                table: "RecurringTaskRules",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 151, 68, 39, 35, 32, 242, 46, 90, 222, 158, 36, 71, 1, 68, 171 });

            migrationBuilder.DeleteData(
                table: "RecurringTaskRules",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 151, 68, 39, 35, 32, 242, 46, 90, 222, 158, 36, 71, 1, 68, 172 });

            migrationBuilder.DeleteData(
                table: "RecurringTaskRules",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 151, 68, 39, 35, 32, 242, 46, 90, 222, 158, 36, 71, 1, 68, 173 });
        }
    }
}
