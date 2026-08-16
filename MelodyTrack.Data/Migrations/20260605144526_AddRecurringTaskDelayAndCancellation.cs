using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurringTaskDelayAndCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringTaskExecutions_Users_SkippedByUserId",
                table: "RecurringTaskExecutions");

            migrationBuilder.RenameColumn(
                name: "SkippedByUserId",
                table: "RecurringTaskExecutions",
                newName: "CancelledByUserId");

            migrationBuilder.RenameColumn(
                name: "SkippedAtUtc",
                table: "RecurringTaskExecutions",
                newName: "CancelledAtUtc");

            migrationBuilder.RenameIndex(
                name: "IX_RecurringTaskExecutions_SkippedByUserId",
                table: "RecurringTaskExecutions",
                newName: "IX_RecurringTaskExecutions_CancelledByUserId");

            migrationBuilder.AddColumn<DateTime>(
                name: "DelayedAtUtc",
                table: "RecurringTaskExecutions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "DelayedByUserId",
                table: "RecurringTaskExecutions",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DelayedUntilUtc",
                table: "RecurringTaskExecutions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringTaskExecutions_Users_CancelledByUserId",
                table: "RecurringTaskExecutions",
                column: "CancelledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringTaskExecutions_DelayedByUserId",
                table: "RecurringTaskExecutions",
                column: "DelayedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringTaskExecutions_Users_DelayedByUserId",
                table: "RecurringTaskExecutions",
                column: "DelayedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurringTaskExecutions_Users_CancelledByUserId",
                table: "RecurringTaskExecutions");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringTaskExecutions_Users_DelayedByUserId",
                table: "RecurringTaskExecutions");

            migrationBuilder.DropIndex(
                name: "IX_RecurringTaskExecutions_DelayedByUserId",
                table: "RecurringTaskExecutions");

            migrationBuilder.DropColumn(
                name: "DelayedAtUtc",
                table: "RecurringTaskExecutions");

            migrationBuilder.DropColumn(
                name: "DelayedByUserId",
                table: "RecurringTaskExecutions");

            migrationBuilder.DropColumn(
                name: "DelayedUntilUtc",
                table: "RecurringTaskExecutions");

            migrationBuilder.RenameColumn(
                name: "CancelledAtUtc",
                table: "RecurringTaskExecutions",
                newName: "SkippedAtUtc");

            migrationBuilder.RenameColumn(
                name: "CancelledByUserId",
                table: "RecurringTaskExecutions",
                newName: "SkippedByUserId");

            migrationBuilder.RenameIndex(
                name: "IX_RecurringTaskExecutions_CancelledByUserId",
                table: "RecurringTaskExecutions",
                newName: "IX_RecurringTaskExecutions_SkippedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringTaskExecutions_Users_SkippedByUserId",
                table: "RecurringTaskExecutions",
                column: "SkippedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
