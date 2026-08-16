using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260513123000_AddRequestReplays")]
    public partial class AddRequestReplays : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequestReplays",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ReplayKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResponseEntityId = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestReplays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequestReplays_Endpoint_ReplayKey",
                table: "RequestReplays",
                columns: new[] { "Endpoint", "ReplayKey" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequestReplays");
        }
    }
}
