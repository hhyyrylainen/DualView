using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteScannedCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentlyScannedUrl",
                table: "DownloadGalleries");

            migrationBuilder.AddColumn<string>(
                name: "CurrentlyScannedUrl",
                table: "ScannedCollections",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GalleryUrl",
                table: "ScannedCollections",
                type: "TEXT",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "ScanFailed",
                table: "ScannedCollections",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                table: "FoundMedia",
                type: "TEXT",
                maxLength: 4096,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ImpersonationHeaders",
                table: "FoundMedia",
                type: "TEXT",
                maxLength: 16384,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideName",
                table: "FoundMedia",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Selected",
                table: "FoundMedia",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "FoundMedia",
                type: "TEXT",
                maxLength: 4096,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentlyScannedUrl",
                table: "ScannedCollections");

            migrationBuilder.DropColumn(
                name: "GalleryUrl",
                table: "ScannedCollections");

            migrationBuilder.DropColumn(
                name: "ScanFailed",
                table: "ScannedCollections");

            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                table: "FoundMedia");

            migrationBuilder.DropColumn(
                name: "ImpersonationHeaders",
                table: "FoundMedia");

            migrationBuilder.DropColumn(
                name: "OverrideName",
                table: "FoundMedia");

            migrationBuilder.DropColumn(
                name: "Selected",
                table: "FoundMedia");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "FoundMedia");

            migrationBuilder.AddColumn<string>(
                name: "CurrentlyScannedUrl",
                table: "DownloadGalleries",
                type: "TEXT",
                maxLength: 1024,
                nullable: true);
        }
    }
}
