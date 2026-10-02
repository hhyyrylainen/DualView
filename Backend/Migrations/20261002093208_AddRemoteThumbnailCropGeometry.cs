using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoteThumbnailCropGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ThumbnailCropHeight",
                table: "FoundMedia",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThumbnailCropWidth",
                table: "FoundMedia",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThumbnailCropX",
                table: "FoundMedia",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ThumbnailCropY",
                table: "FoundMedia",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailCropHeight",
                table: "FoundMedia");

            migrationBuilder.DropColumn(
                name: "ThumbnailCropWidth",
                table: "FoundMedia");

            migrationBuilder.DropColumn(
                name: "ThumbnailCropX",
                table: "FoundMedia");

            migrationBuilder.DropColumn(
                name: "ThumbnailCropY",
                table: "FoundMedia");
        }
    }
}
