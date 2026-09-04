using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExplicitTableForRecurrenceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurrenceRules_RecurrenceType_RecurrenceTypeId",
                table: "RecurrenceRules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RecurrenceType",
                table: "RecurrenceType");

            migrationBuilder.RenameTable(
                name: "RecurrenceType",
                newName: "RecurrenceTypes");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RecurrenceTypes",
                table: "RecurrenceTypes",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurrenceRules_RecurrenceTypes_RecurrenceTypeId",
                table: "RecurrenceRules",
                column: "RecurrenceTypeId",
                principalTable: "RecurrenceTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RecurrenceRules_RecurrenceTypes_RecurrenceTypeId",
                table: "RecurrenceRules");

            migrationBuilder.DropPrimaryKey(
                name: "PK_RecurrenceTypes",
                table: "RecurrenceTypes");

            migrationBuilder.RenameTable(
                name: "RecurrenceTypes",
                newName: "RecurrenceType");

            migrationBuilder.AddPrimaryKey(
                name: "PK_RecurrenceType",
                table: "RecurrenceType",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RecurrenceRules_RecurrenceType_RecurrenceTypeId",
                table: "RecurrenceRules",
                column: "RecurrenceTypeId",
                principalTable: "RecurrenceType",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
