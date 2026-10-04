using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceHomeworkBbcodeWithStructuredDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "HomeworkContent",
                table: "CourseThemes",
                newName: "HomeworkDocument");

            migrationBuilder.Sql("UPDATE \"CourseThemes\" SET \"LessonContent\" = NULL, \"HomeworkDocument\" = NULL;");

            migrationBuilder.CreateTable(
                name: "CourseThemePracticeExercises",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    CourseThemeId = table.Column<byte[]>(type: "bytea", nullable: false),
                    Type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Configuration = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseThemePracticeExercises", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseThemePracticeExercises_CourseThemes_CourseThemeId",
                        column: x => x.CourseThemeId,
                        principalTable: "CourseThemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CourseThemePracticeExercises_CourseThemeId",
                table: "CourseThemePracticeExercises",
                column: "CourseThemeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CourseThemePracticeExercises");

            migrationBuilder.RenameColumn(
                name: "HomeworkDocument",
                table: "CourseThemes",
                newName: "HomeworkContent");
        }
    }
}
