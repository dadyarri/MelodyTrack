using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseProgressFoundations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CourseThemeId",
                table: "Appointments",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Courses",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Courses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CourseBlocks",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    CourseId = table.Column<byte[]>(type: "bytea", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseBlocks_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourseEnrollments",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    ClientId = table.Column<byte[]>(type: "bytea", nullable: false),
                    CourseId = table.Column<byte[]>(type: "bytea", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EarnedEvolutionPoints = table.Column<int>(type: "integer", nullable: false),
                    SpentEvolutionPoints = table.Column<int>(type: "integer", nullable: false),
                    EarnedExperiencePoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseEnrollments_Clients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "Clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseEnrollments_Courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "Courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourseBranches",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    BlockId = table.Column<byte[]>(type: "bytea", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseBranches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseBranches_CourseBlocks_BlockId",
                        column: x => x.BlockId,
                        principalTable: "CourseBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourseThemes",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    BranchId = table.Column<byte[]>(type: "bytea", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    LessonContent = table.Column<string>(type: "text", nullable: true),
                    HomeworkContent = table.Column<string>(type: "text", nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    UnlockCostPoints = table.Column<int>(type: "integer", nullable: false),
                    EvolutionPointsReward = table.Column<int>(type: "integer", nullable: false),
                    ExperiencePointsReward = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseThemes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseThemes_CourseBranches_BranchId",
                        column: x => x.BranchId,
                        principalTable: "CourseBranches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourseEnrollmentThemes",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    EnrollmentId = table.Column<byte[]>(type: "bytea", nullable: false),
                    CourseThemeId = table.Column<byte[]>(type: "bytea", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    UnlockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    WaitingForHomeworkAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SpentEvolutionPoints = table.Column<int>(type: "integer", nullable: false),
                    EarnedEvolutionPoints = table.Column<int>(type: "integer", nullable: false),
                    EarnedExperiencePoints = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseEnrollmentThemes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseEnrollmentThemes_CourseEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "CourseEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CourseEnrollmentThemes_CourseThemes_CourseThemeId",
                        column: x => x.CourseThemeId,
                        principalTable: "CourseThemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CourseThemeDependencies",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    ThemeId = table.Column<byte[]>(type: "bytea", nullable: false),
                    DependsOnThemeId = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourseThemeDependencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CourseThemeDependencies_CourseThemes_DependsOnThemeId",
                        column: x => x.DependsOnThemeId,
                        principalTable: "CourseThemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourseThemeDependencies_CourseThemes_ThemeId",
                        column: x => x.ThemeId,
                        principalTable: "CourseThemes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_CourseThemeId",
                table: "Appointments",
                column: "CourseThemeId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseBlocks_CourseId_Order",
                table: "CourseBlocks",
                columns: new[] { "CourseId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseBranches_BlockId_Order",
                table: "CourseBranches",
                columns: new[] { "BlockId", "Order" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollments_ClientId_CourseId",
                table: "CourseEnrollments",
                columns: new[] { "ClientId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollments_CourseId",
                table: "CourseEnrollments",
                column: "CourseId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollmentThemes_CourseThemeId",
                table: "CourseEnrollmentThemes",
                column: "CourseThemeId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseEnrollmentThemes_EnrollmentId_CourseThemeId",
                table: "CourseEnrollmentThemes",
                columns: new[] { "EnrollmentId", "CourseThemeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Courses_Name",
                table: "Courses",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CourseThemeDependencies_DependsOnThemeId",
                table: "CourseThemeDependencies",
                column: "DependsOnThemeId");

            migrationBuilder.CreateIndex(
                name: "IX_CourseThemeDependencies_ThemeId_DependsOnThemeId",
                table: "CourseThemeDependencies",
                columns: new[] { "ThemeId", "DependsOnThemeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourseThemes_BranchId_Order",
                table: "CourseThemes",
                columns: new[] { "BranchId", "Order" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Appointments_CourseThemes_CourseThemeId",
                table: "Appointments",
                column: "CourseThemeId",
                principalTable: "CourseThemes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Appointments_CourseThemes_CourseThemeId",
                table: "Appointments");

            migrationBuilder.DropTable(
                name: "CourseEnrollmentThemes");

            migrationBuilder.DropTable(
                name: "CourseThemeDependencies");

            migrationBuilder.DropTable(
                name: "CourseEnrollments");

            migrationBuilder.DropTable(
                name: "CourseThemes");

            migrationBuilder.DropTable(
                name: "CourseBranches");

            migrationBuilder.DropTable(
                name: "CourseBlocks");

            migrationBuilder.DropTable(
                name: "Courses");

            migrationBuilder.DropIndex(
                name: "IX_Appointments_CourseThemeId",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "CourseThemeId",
                table: "Appointments");
        }
    }
}
