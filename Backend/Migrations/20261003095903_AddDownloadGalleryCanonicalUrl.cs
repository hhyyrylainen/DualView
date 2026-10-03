using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadGalleryCanonicalUrl : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                table: "DownloadGalleries",
                type: "TEXT",
                maxLength: 1024,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("UPDATE DownloadGalleries SET CanonicalUrl = GalleryUrl");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadGalleries_CanonicalUrl",
                table: "DownloadGalleries",
                column: "CanonicalUrl",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DownloadGalleries_CanonicalUrl",
                table: "DownloadGalleries");

            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                table: "DownloadGalleries");
        }
    }
}
