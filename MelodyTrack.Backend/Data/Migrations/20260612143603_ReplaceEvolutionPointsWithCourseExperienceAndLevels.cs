using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceEvolutionPointsWithCourseExperienceAndLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClientLevelReward",
                table: "CourseThemes");

            migrationBuilder.DropColumn(
                name: "EarnedClientLevels",
                table: "CourseEnrollmentThemes");

            migrationBuilder.DropColumn(
                name: "ClientLevel",
                table: "CourseEnrollments");

            migrationBuilder.CreateTable(
                name: "CourseLevels",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    CourseId = table.Column<byte[]>(type: "bytea", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequiredExperiencePoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseLevels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseLevels_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseLevels_CourseId_Order",
                table: "CourseLevels",
                columns: new[] { "CourseId", "Order" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseLevels");

            migrationBuilder.AddColumn<int>(
                name: "ClientLevelReward",
                table: "CourseThemes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "EarnedClientLevels",
                table: "CourseEnrollmentThemes",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClientLevel",
                table: "CourseEnrollments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }
    }
}
