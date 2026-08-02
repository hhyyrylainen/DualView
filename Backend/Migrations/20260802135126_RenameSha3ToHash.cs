using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class RenameSha3ToHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "HashSha3",
                table: "MediaFiles",
                newName: "Hash");

            migrationBuilder.RenameIndex(
                name: "IX_MediaFiles_HashSha3",
                table: "MediaFiles",
                newName: "IX_MediaFiles_Hash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Hash",
                table: "MediaFiles",
                newName: "HashSha3");

            migrationBuilder.RenameIndex(
                name: "IX_MediaFiles_Hash",
                table: "MediaFiles",
                newName: "IX_MediaFiles_HashSha3");
        }
    }
}
