using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenClientPortalCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PinCode",
                table: "ClientPortalLoginLinks");

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
                name: "RevokedAtUtc",
                table: "ClientPortalLoginLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TokenHash",
                table: "ClientPortalLoginLinks",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalLoginLinks_TokenHash",
                table: "ClientPortalLoginLinks",
                column: "TokenHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientPortalLoginLinks_TokenHash",
                table: "ClientPortalLoginLinks");

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
                name: "RevokedAtUtc",
                table: "ClientPortalLoginLinks");

            migrationBuilder.DropColumn(
                name: "TokenHash",
                table: "ClientPortalLoginLinks");

            migrationBuilder.AddColumn<string>(
                name: "PinCode",
                table: "ClientPortalLoginLinks",
                type: "text",
                nullable: true);
        }
    }
}
