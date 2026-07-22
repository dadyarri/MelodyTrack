using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddServicePublicName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicName",
                table: "Services",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PublicName",
                table: "Services");
        }
    }
}
