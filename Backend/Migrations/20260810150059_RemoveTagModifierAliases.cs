using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTagModifierAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TagModifierAliases");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TagModifierAliases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModifierId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagModifierAliases", x => x.Name);
                    table.ForeignKey(
                        name: "FK_TagModifierAliases_TagModifiers_ModifierId",
                        column: x => x.ModifierId,
                        principalTable: "TagModifiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TagModifierAliases_ModifierId",
                table: "TagModifierAliases",
                column: "ModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_TagModifierAliases_Name",
                table: "TagModifierAliases",
                column: "Name",
                unique: true);
        }
    }
}
