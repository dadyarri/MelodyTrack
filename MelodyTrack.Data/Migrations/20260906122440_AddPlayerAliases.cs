using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace MelodyTrack.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PortalAlias",
                table: "Users",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AliasBardicTerms",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AliasBardicTerms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AliasCreatures",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AliasCreatures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AliasEpithets",
                columns: table => new
                {
                    Id = table.Column<byte[]>(type: "bytea", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AliasEpithets", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "AliasBardicTerms",
                columns: new[] { "Id", "IsEnabled", "Value" },
                values: new object[,]
                {
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 1 }, true, "Бард" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 34 }, true, "Орфей" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 33 }, true, "Композитор" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 32 }, true, "Импровизатор" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 8 }, true, "Трубадур" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 7 }, true, "Сказитель" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 9 }, true, "Музыкант" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 5 }, true, "Маэстро" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 4 }, true, "Виртуоз" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 3 }, true, "Скальд" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 2 }, true, "Менестрель" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 44, 6 }, true, "Певец" }
                });

            migrationBuilder.InsertData(
                table: "AliasCreatures",
                columns: new[] { "Id", "IsEnabled", "Value" },
                values: new object[,]
                {
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 8 }, true, "Сфинкс" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 34 }, true, "Дух" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 33 }, true, "Левиафан" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 32 }, true, "Гиппогриф" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 9 }, true, "Голем" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 7 }, true, "Кентавр" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 6 }, true, "Пегас" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 5 }, true, "Василиск" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 4 }, true, "Единорог" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 3 }, true, "Феникс" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 2 }, true, "Дракон" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 48, 1 }, true, "Грифон" }
                });

            migrationBuilder.InsertData(
                table: "AliasEpithets",
                columns: new[] { "Id", "IsEnabled", "Value" },
                values: new object[,]
                {
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 32 }, true, "Таинственный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 9 }, true, "Благородный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 8 }, true, "Отважный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 7 }, true, "Солнечный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 6 }, true, "Лунный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 4 }, true, "Чародейный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 33 }, true, "Изумрудный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 3 }, true, "Мудрый" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 2 }, true, "Доблестный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 1 }, true, "Сияющий" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 5 }, true, "Звёздный" },
                    { new byte[] { 1, 152, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 34 }, true, "Серебряный" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_PortalAlias",
                table: "Users",
                column: "PortalAlias",
                unique: true,
                filter: "\"PortalAlias\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_AliasBardicTerms_Value",
                table: "AliasBardicTerms",
                column: "Value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AliasCreatures_Value",
                table: "AliasCreatures",
                column: "Value",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AliasEpithets_Value",
                table: "AliasEpithets",
                column: "Value",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AliasBardicTerms");

            migrationBuilder.DropTable(
                name: "AliasCreatures");

            migrationBuilder.DropTable(
                name: "AliasEpithets");

            migrationBuilder.DropIndex(
                name: "IX_Users_PortalAlias",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PortalAlias",
                table: "Users");
        }
    }
}
