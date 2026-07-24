using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeRequestReplaysToCallerAndPayload : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestReplays_Endpoint_ReplayKey",
                table: "RequestReplays");

            // Replay rows are short-lived transport state and cannot be safely attributed
            // or fingerprinted retroactively. Discard them instead of creating ambiguous
            // caller-less records during the schema transition.
            migrationBuilder.Sql("""DELETE FROM "RequestReplays";""");

            migrationBuilder.AddColumn<byte[]>(
                name: "CallerId",
                table: "RequestReplays",
                type: "bytea",
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "RequestReplays",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_RequestReplays_Endpoint_CallerId_ReplayKey",
                table: "RequestReplays",
                columns: new[] { "Endpoint", "CallerId", "ReplayKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RequestReplays_Endpoint_CallerId_ReplayKey",
                table: "RequestReplays");

            migrationBuilder.Sql("""DELETE FROM "RequestReplays";""");

            migrationBuilder.DropColumn(
                name: "CallerId",
                table: "RequestReplays");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "RequestReplays");

            migrationBuilder.CreateIndex(
                name: "IX_RequestReplays_Endpoint_ReplayKey",
                table: "RequestReplays",
                columns: new[] { "Endpoint", "ReplayKey" },
                unique: true);
        }
    }
}
