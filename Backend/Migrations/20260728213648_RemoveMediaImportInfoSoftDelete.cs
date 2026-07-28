using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class RemoveMediaImportInfoSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "MediaImportInfos");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadGalleries_GalleryUrl",
                table: "DownloadGalleries",
                column: "GalleryUrl",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DownloadGalleries_GalleryUrl",
                table: "DownloadGalleries");

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "MediaImportInfos",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
