using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOptionalProviderToAppointmentRecurrenceRule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "ProviderId",
                table: "RecurrenceRules",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurrenceRules_ProviderId",
                table: "RecurrenceRules",
                column: "ProviderId");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurrenceRules_Users_ProviderId",
                table: "RecurrenceRules",
                column: "ProviderId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurrenceRules_Users_ProviderId",
                table: "RecurrenceRules");

            migrationBuilder.DropIndex(
                name: "IX_RecurrenceRules_ProviderId",
                table: "RecurrenceRules");

            migrationBuilder.DropColumn(
                name: "ProviderId",
                table: "RecurrenceRules");
        }
    }
}
