using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    RoleName = table.Column<int>(type: "integer", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InviteCodes",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Code = table.Column<byte[]>(type: "bytea", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WasUsed = table.Column<bool>(type: "boolean", nullable: false),
                    RoleId = table.Column<byte[]>(type: "bytea", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InviteCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InviteCodes_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    FirstName = table.Column<string>(type: "text", nullable: false),
                    LastName = table.Column<string>(type: "text", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    Password = table.Column<string>(type: "text", nullable: false),
                    TotpSecret = table.Column<string>(type: "text", nullable: true),
                    RoleId = table.Column<byte[]>(type: "bytea", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecoveryCodes",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Code = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<byte[]>(type: "bytea", nullable: false),
                    WasUsed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecoveryCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecoveryCodes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "DisplayName", "RoleName" },
                values: new object[,]
                {
                    { new byte[] { 1, 153, 237, 189, 136, 239, 85, 185, 220, 69, 200, 138, 71, 160, 55, 244 }, "Суперпользователь", 0 },
                    { new byte[] { 1, 153, 237, 189, 164, 92, 201, 142, 60, 167, 216, 236, 125, 205, 69, 213 }, "Администратор", 1 },
                    { new byte[] { 1, 153, 237, 189, 179, 9, 105, 35, 34, 182, 33, 139, 189, 171, 222, 9 }, "Пользователь", 2 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_InviteCodes_RoleId",
                table: "InviteCodes",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_RecoveryCodes_UserId",
                table: "RecoveryCodes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InviteCodes");

            migrationBuilder.DropTable(
                name: "RecoveryCodes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
