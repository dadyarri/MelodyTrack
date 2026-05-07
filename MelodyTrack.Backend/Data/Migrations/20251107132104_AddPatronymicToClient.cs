using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPatronymicToClient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Patronymic",
                table: "Clients",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Patronymic",
                table: "Clients");
        }
    }
}
