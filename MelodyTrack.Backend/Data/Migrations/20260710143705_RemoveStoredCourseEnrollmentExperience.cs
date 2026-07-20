using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveStoredCourseEnrollmentExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EarnedExperiencePoints",
                table: "CourseEnrollmentThemes");

            migrationBuilder.DropColumn(
                name: "EarnedExperiencePoints",
                table: "CourseEnrollments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EarnedExperiencePoints",
                table: "CourseEnrollmentThemes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EarnedExperiencePoints",
                table: "CourseEnrollments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
