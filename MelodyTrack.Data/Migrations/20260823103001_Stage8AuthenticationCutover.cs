using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stage8AuthenticationCutover : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE "Sessions"
                SET "WasRevoked" = TRUE
                WHERE "WasRevoked" = FALSE;

                UPDATE "PasswordRestorationRequests"
                SET "WasUsed" = TRUE
                WHERE "WasUsed" = FALSE;

                UPDATE "Users" AS users
                SET "Password" = '!stage8-reset-required!'
                FROM "Roles" AS roles
                WHERE users."RoleId" = roles."Id"
                  AND roles."RoleName" <> 8;

                DELETE FROM "ClientPortalSavedIdentityReferences";

                UPDATE "ClientPortalLoginLinks"
                SET "TokenHash" = NULL,
                    "PinHash" = NULL,
                    "PinSetAtUtc" = NULL,
                    "FailedPinAttempts" = 0,
                    "LastFailedPinAttemptAtUtc" = NULL;
                """);

            migrationBuilder.DropColumn(
                name: "LockedUntil",
                table: "Users");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LockedUntil",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
