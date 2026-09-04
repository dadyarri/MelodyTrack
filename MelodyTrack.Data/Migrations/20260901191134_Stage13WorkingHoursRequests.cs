using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stage13WorkingHoursRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WorkingHoursRequests",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    RequesterUserId = table.Column<byte[]>(type: "bytea", nullable: false),
                    SubjectUserId = table.Column<byte[]>(type: "bytea", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedBySuperuserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    DecisionMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingHoursRequests", x => x.Id);
                    table.CheckConstraint("CK_WorkingHoursRequests_Version", "\"Version\" > 0");
                });

            migrationBuilder.CreateTable(
                name: "WorkingHoursRequestDays",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    WorkingHoursRequestId = table.Column<byte[]>(type: "bytea", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    IsWorkingDay = table.Column<bool>(type: "boolean", nullable: false),
                    StartMinuteOfDay = table.Column<int>(type: "integer", nullable: false),
                    EndMinuteOfDay = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkingHoursRequestDays", x => x.Id);
                    table.CheckConstraint("CK_WorkingHoursRequestDays_Minutes", "\"StartMinuteOfDay\" >= 0 AND \"StartMinuteOfDay\" < 1440 AND \"EndMinuteOfDay\" > 0 AND \"EndMinuteOfDay\" <= 1440 AND \"StartMinuteOfDay\" < \"EndMinuteOfDay\"");
                    table.ForeignKey(
                        name: "FK_WorkingHoursRequestDays_WorkingHoursRequests_WorkingHoursRe~",
                        column: x => x.WorkingHoursRequestId,
                        principalTable: "WorkingHoursRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHoursRequestDays_WorkingHoursRequestId_DayOfWeek",
                table: "WorkingHoursRequestDays",
                columns: new[] { "WorkingHoursRequestId", "DayOfWeek" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHoursRequests_RequesterUserId_CreatedAtUtc",
                table: "WorkingHoursRequests",
                columns: new[] { "RequesterUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkingHoursRequests_SubjectUserId_Status",
                table: "WorkingHoursRequests",
                columns: new[] { "SubjectUserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkingHoursRequestDays");

            migrationBuilder.DropTable(
                name: "WorkingHoursRequests");
        }
    }
}
