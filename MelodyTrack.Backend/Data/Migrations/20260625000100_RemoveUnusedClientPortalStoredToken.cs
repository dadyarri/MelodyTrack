using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveUnusedClientPortalStoredToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ClientPortalLoginLinks_Token",
                table: "ClientPortalLoginLinks");

            migrationBuilder.DropColumn(
                name: "Token",
                table: "ClientPortalLoginLinks");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Token",
                table: "ClientPortalLoginLinks",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalLoginLinks_Token",
                table: "ClientPortalLoginLinks",
                column: "Token",
                unique: true);
        }
    }
}
