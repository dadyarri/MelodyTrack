using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceEvolutionPointsWithClientLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EvolutionPointsReward",
                table: "CourseThemes");

            migrationBuilder.DropColumn(
                name: "EarnedEvolutionPoints",
                table: "CourseEnrollmentThemes");

            migrationBuilder.DropColumn(
                name: "EarnedEvolutionPoints",
                table: "CourseEnrollments");

            migrationBuilder.RenameColumn(
                name: "UnlockCostPoints",
                table: "CourseThemes",
                newName: "ClientLevelReward");

            migrationBuilder.RenameColumn(
                name: "SpentEvolutionPoints",
                table: "CourseEnrollmentThemes",
                newName: "EarnedClientLevels");

            migrationBuilder.RenameColumn(
                name: "SpentEvolutionPoints",
                table: "CourseEnrollments",
                newName: "ClientLevel");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ClientLevelReward",
                table: "CourseThemes",
                newName: "UnlockCostPoints");

            migrationBuilder.RenameColumn(
                name: "EarnedClientLevels",
                table: "CourseEnrollmentThemes",
                newName: "SpentEvolutionPoints");

            migrationBuilder.RenameColumn(
                name: "ClientLevel",
                table: "CourseEnrollments",
                newName: "SpentEvolutionPoints");

            migrationBuilder.AddColumn<int>(
                name: "EvolutionPointsReward",
                table: "CourseThemes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EarnedEvolutionPoints",
                table: "CourseEnrollmentThemes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EarnedEvolutionPoints",
                table: "CourseEnrollments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
