using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class PersistClientPortalPins : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ValidUntil",
                table: "ClientPortalLoginLinks");

            migrationBuilder.DropColumn(
                name: "WasUsed",
                table: "ClientPortalLoginLinks");

            migrationBuilder.AddColumn<string>(
                name: "PinCode",
                table: "ClientPortalLoginLinks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PinSetAtUtc",
                table: "ClientPortalLoginLinks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PinCode",
                table: "ClientPortalLoginLinks");

            migrationBuilder.DropColumn(
                name: "PinSetAtUtc",
                table: "ClientPortalLoginLinks");

            migrationBuilder.AddColumn<DateTime>(
                name: "ValidUntil",
                table: "ClientPortalLoginLinks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<bool>(
                name: "WasUsed",
                table: "ClientPortalLoginLinks",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
