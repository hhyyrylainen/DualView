using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddTagSuperAlias : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTemporary",
                table: "MediaFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "TagSuperAliases",
                columns: table => new
                {
                    Alias = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Expanded = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagSuperAliases", x => x.Alias);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagSuperAliases_Alias",
                table: "TagSuperAliases",
                column: "Alias",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagSuperAliases");

            migrationBuilder.DropColumn(
                name: "IsTemporary",
                table: "MediaFiles");
        }
    }
}
