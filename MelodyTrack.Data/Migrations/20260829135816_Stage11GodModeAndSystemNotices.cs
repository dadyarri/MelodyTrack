using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stage11GodModeAndSystemNotices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "PasswordResetRequired",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SystemNotices",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Severity = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Dismissible = table.Column<bool>(type: "boolean", nullable: false),
                    AudienceType = table.Column<int>(type: "integer", nullable: false),
                    ShowBeforeAuthentication = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemNotices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemNoticeRecipients",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    NoticeId = table.Column<byte[]>(type: "bytea", nullable: false),
                    UserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    ClientId = table.Column<byte[]>(type: "bytea", nullable: true),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DismissedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemNoticeRecipients", x => x.Id);
                    table.CheckConstraint("CK_SystemNoticeRecipients_ExactlyOneRecipient", "(\"UserId\" IS NOT NULL AND \"ClientId\" IS NULL) OR (\"UserId\" IS NULL AND \"ClientId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_SystemNoticeRecipients_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SystemNoticeRecipients_SystemNotices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "SystemNotices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SystemNoticeRecipients_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SystemNoticeRecipients_ClientId",
                table: "SystemNoticeRecipients",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SystemNoticeRecipients_NoticeId_ClientId",
                table: "SystemNoticeRecipients",
                columns: new[] { "NoticeId", "ClientId" },
                unique: true,
                filter: "\"ClientId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SystemNoticeRecipients_NoticeId_UserId",
                table: "SystemNoticeRecipients",
                columns: new[] { "NoticeId", "UserId" },
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SystemNoticeRecipients_UserId",
                table: "SystemNoticeRecipients",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemNoticeRecipients");

            migrationBuilder.DropTable(
                name: "SystemNotices");

            migrationBuilder.DropColumn(
                name: "PasswordResetRequired",
                table: "Users");
        }
    }
}
