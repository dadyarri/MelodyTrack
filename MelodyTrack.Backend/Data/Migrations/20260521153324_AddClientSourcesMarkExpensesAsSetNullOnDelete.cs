using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MelodyTrack.Backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddClientSourcesMarkExpensesAsSetNullOnDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_ExpenseCategory_CategoryId",
                table: "Expenses");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ExpenseCategory",
                table: "ExpenseCategory");

            migrationBuilder.RenameTable(
                name: "ExpenseCategory",
                newName: "ExpenseCategories");

            migrationBuilder.AddColumn<byte[]>(
                name: "SourceId",
                table: "Clients",
                type: "bytea",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ExpenseCategories",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ExpenseCategories",
                table: "ExpenseCategories",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "ClientSources",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Clients_SourceId",
                table: "Clients",
                column: "SourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Clients_ClientSources_SourceId",
                table: "Clients",
                column: "SourceId",
                principalTable: "ClientSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_ExpenseCategories_CategoryId",
                table: "Expenses",
                column: "CategoryId",
                principalTable: "ExpenseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Clients_ClientSources_SourceId",
                table: "Clients");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_ExpenseCategories_CategoryId",
                table: "Expenses");

            migrationBuilder.DropTable(
                name: "ClientSources");

            migrationBuilder.DropIndex(
                name: "IX_Clients_SourceId",
                table: "Clients");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ExpenseCategories",
                table: "ExpenseCategories");

            migrationBuilder.DropColumn(
                name: "SourceId",
                table: "Clients");

            migrationBuilder.RenameTable(
                name: "ExpenseCategories",
                newName: "ExpenseCategory");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "ExpenseCategory",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AddPrimaryKey(
                name: "PK_ExpenseCategory",
                table: "ExpenseCategory",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_ExpenseCategory_CategoryId",
                table: "Expenses",
                column: "CategoryId",
                principalTable: "ExpenseCategory",
                principalColumn: "Id");
        }
    }
}
