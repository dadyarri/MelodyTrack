using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedClientPortalIdentities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClientPortalSavedIdentityReferences",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    LoginLinkId = table.Column<byte[]>(type: "bytea", nullable: false),
                    ReferenceHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientPortalSavedIdentityReferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientPortalSavedIdentityReferences_ClientPortalLoginLinks_~",
                        column: x => x.LoginLinkId,
                        principalTable: "ClientPortalLoginLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalSavedIdentityReferences_LoginLinkId",
                table: "ClientPortalSavedIdentityReferences",
                column: "LoginLinkId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientPortalSavedIdentityReferences_ReferenceHash",
                table: "ClientPortalSavedIdentityReferences",
                column: "ReferenceHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientPortalSavedIdentityReferences");
        }
    }
}
