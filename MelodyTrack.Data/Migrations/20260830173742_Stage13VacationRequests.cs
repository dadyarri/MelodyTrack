using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class Stage13VacationRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VacationRequests",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    RequesterPrincipalType = table.Column<string>(type: "text", nullable: false),
                    RequesterId = table.Column<byte[]>(type: "bytea", nullable: false),
                    SubjectType = table.Column<string>(type: "text", nullable: false),
                    SubjectId = table.Column<byte[]>(type: "bytea", nullable: false),
                    RequestedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestedEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    RequestMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ProcessedBySuperuserId = table.Column<byte[]>(type: "bytea", nullable: true),
                    DecisionMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ResultingVacationId = table.Column<byte[]>(type: "bytea", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VacationRequests", x => x.Id);
                    table.CheckConstraint("CK_VacationRequests_Range", "\"RequestedStart\" <= \"RequestedEnd\"");
                    table.CheckConstraint("CK_VacationRequests_Version", "\"Version\" > 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_VacationRequests_RequesterPrincipalType_RequesterId_Created~",
                table: "VacationRequests",
                columns: new[] { "RequesterPrincipalType", "RequesterId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_VacationRequests_SubjectType_SubjectId_Status",
                table: "VacationRequests",
                columns: new[] { "SubjectType", "SubjectId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VacationRequests");
        }
    }
}
