using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemovePortalPinAndSavedIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Client is the persisted role flag 8; keep this migration independent of future enum changes.
            migrationBuilder.Sql(
                """
                UPDATE "Sessions" AS sessions
                SET "WasRevoked" = TRUE
                FROM "Users" AS users
                JOIN "Roles" AS roles ON users."RoleId" = roles."Id"
                WHERE sessions."UserId" = users."Id"
                  AND (roles."RoleName" & 8) <> 0
                  AND sessions."WasRevoked" = FALSE;
                """);

            migrationBuilder.DropTable(
                name: "ClientPortalSavedIdentityReferences");

            migrationBuilder.Sql("""DELETE FROM "ClientPortalLoginLinks";""");

            migrationBuilder.DropColumn(
                name: "FailedPinAttempts",
                table: "ClientPortalLoginLinks");

            migrationBuilder.DropColumn(
                name: "LastFailedPinAttemptAtUtc",
                table: "ClientPortalLoginLinks");

            migrationBuilder.DropColumn(
                name: "PinHash",
                table: "ClientPortalLoginLinks");

            migrationBuilder.DropColumn(
                name: "PinSetAtUtc",
                table: "ClientPortalLoginLinks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling back the schema must not restore old links or reactivate revoked sessions.
            migrationBuilder.AddColumn<int>(
                name: "FailedPinAttempts",
                table: "ClientPortalLoginLinks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastFailedPinAttemptAtUtc",
                table: "ClientPortalLoginLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinHash",
                table: "ClientPortalLoginLinks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinSetAtUtc",
                table: "ClientPortalLoginLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClientPortalSavedIdentityReferences",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    LoginLinkId = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReferenceHash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientPortalSavedIdentityReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientPortalSavedIdentityReferences_ClientPortalLoginLinks_~",
                        column: x => x.LoginLinkId,
                        principalTable: "ClientPortalLoginLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalSavedIdentityReferences_LoginLinkId",
                table: "ClientPortalSavedIdentityReferences",
                column: "LoginLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalSavedIdentityReferences_ReferenceHash",
                table: "ClientPortalSavedIdentityReferences",
                column: "ReferenceHash",
                unique: true);
        }
    }
}
