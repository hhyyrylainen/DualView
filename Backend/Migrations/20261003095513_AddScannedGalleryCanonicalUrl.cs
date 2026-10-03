using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddScannedGalleryCanonicalUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                table: "ScannedCollections",
                type: "TEXT",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE ScannedCollections SET CanonicalUrl = GalleryUrl");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedCollections_CanonicalUrl",
                table: "ScannedCollections",
                column: "CanonicalUrl");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScannedCollections_CanonicalUrl",
                table: "ScannedCollections");

            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                table: "ScannedCollections");
        }
    }
}
