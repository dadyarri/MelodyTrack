using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateValuesOfRolesEnum : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 153, 237, 189, 136, 239, 85, 185, 220, 69, 200, 138, 71, 160, 55, 244 },
                column: "RoleName",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 153, 237, 189, 164, 92, 201, 142, 60, 167, 216, 236, 125, 205, 69, 213 },
                column: "RoleName",
                value: 2);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 153, 237, 189, 179, 9, 105, 35, 34, 182, 33, 139, 189, 171, 222, 9 },
                column: "RoleName",
                value: 4);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 153, 237, 189, 136, 239, 85, 185, 220, 69, 200, 138, 71, 160, 55, 244 },
                column: "RoleName",
                value: 0);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 153, 237, 189, 164, 92, 201, 142, 60, 167, 216, 236, 125, 205, 69, 213 },
                column: "RoleName",
                value: 1);

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: new byte[] { 1, 153, 237, 189, 179, 9, 105, 35, 34, 182, 33, 139, 189, 171, 222, 9 },
                column: "RoleName",
                value: 2);
        }
    }
}
