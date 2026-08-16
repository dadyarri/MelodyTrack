using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomTasks",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    ClientId = table.Column<byte[]>(type: "bytea", nullable: true),
                    RecipientName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Telegram = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Vk = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    MessageText = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    DueAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByUserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    CompletedByUserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledByUserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DelayedByUserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    DelayedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DelayedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomTasks_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomTasks_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomTasks_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomTasks_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CustomTasks_Users_DelayedByUserId",
                        column: x => x.DelayedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomTasks_CancelledByUserId",
                table: "CustomTasks",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomTasks_ClientId_DueAtUtc",
                table: "CustomTasks",
                columns: new[] { "ClientId", "DueAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomTasks_CompletedByUserId",
                table: "CustomTasks",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomTasks_CreatedByUserId",
                table: "CustomTasks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomTasks_DelayedByUserId",
                table: "CustomTasks",
                column: "DelayedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomTasks_DelayedUntilUtc",
                table: "CustomTasks",
                column: "DelayedUntilUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomTasks");
        }
    }
}
