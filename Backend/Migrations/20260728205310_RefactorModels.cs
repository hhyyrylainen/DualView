using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class RefactorModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaImportInfos_DownloadFiles_DownloadFileId",
                table: "MediaImportInfos");

            migrationBuilder.DropTable(
                name: "ActionHistories");

            migrationBuilder.DropTable(
                name: "DownloadFiles");

            migrationBuilder.DropTable(
                name: "ImageRegions");

            migrationBuilder.DropTable(
                name: "MediaRatings");

            migrationBuilder.RenameColumn(
                name: "DownloadFileId",
                table: "MediaImportInfos",
                newName: "DownloadGalleryId");

            migrationBuilder.RenameIndex(
                name: "IX_MediaImportInfos_DownloadFileId",
                table: "MediaImportInfos",
                newName: "IX_MediaImportInfos_DownloadGalleryId");

            migrationBuilder.AddColumn<string>(
                name: "PreferredName",
                table: "MediaImportInfos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TagsString",
                table: "MediaImportInfos",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFavorited",
                table: "MediaFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Stars",
                table: "MediaFiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaImportInfos_DownloadGalleries_DownloadGalleryId",
                table: "MediaImportInfos",
                column: "DownloadGalleryId",
                principalTable: "DownloadGalleries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaImportInfos_DownloadGalleries_DownloadGalleryId",
                table: "MediaImportInfos");

            migrationBuilder.DropColumn(
                name: "PreferredName",
                table: "MediaImportInfos");

            migrationBuilder.DropColumn(
                name: "TagsString",
                table: "MediaImportInfos");

            migrationBuilder.DropColumn(
                name: "IsFavorited",
                table: "MediaFiles");

            migrationBuilder.DropColumn(
                name: "Stars",
                table: "MediaFiles");

            migrationBuilder.RenameColumn(
                name: "DownloadGalleryId",
                table: "MediaImportInfos",
                newName: "DownloadFileId");

            migrationBuilder.RenameIndex(
                name: "IX_MediaImportInfos_DownloadGalleryId",
                table: "MediaImportInfos",
                newName: "IX_MediaImportInfos_DownloadFileId");

            migrationBuilder.CreateTable(
                name: "ActionHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    JsonData = table.Column<string>(type: "TEXT", nullable: false),
                    Performed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DownloadGalleryId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileUrl = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    PageReferrer = table.Column<string>(type: "TEXT", nullable: true),
                    PreferredName = table.Column<string>(type: "TEXT", nullable: false),
                    TagsString = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadFiles_DownloadGalleries_DownloadGalleryId",
                        column: x => x.DownloadGalleryId,
                        principalTable: "DownloadGalleries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ImageRegions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    Bottom = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Left = table.Column<int>(type: "INTEGER", nullable: false),
                    Right = table.Column<int>(type: "INTEGER", nullable: false),
                    StartFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    Top = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImageRegions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImageRegions_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaRatings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFavorited = table.Column<bool>(type: "INTEGER", nullable: false),
                    Stars = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaRatings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaRatings_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadFiles_DownloadGalleryId",
                table: "DownloadFiles",
                column: "DownloadGalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_ImageRegions_MediaFileId",
                table: "ImageRegions",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaRatings_MediaFileId",
                table: "MediaRatings",
                column: "MediaFileId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaImportInfos_DownloadFiles_DownloadFileId",
                table: "MediaImportInfos",
                column: "DownloadFileId",
                principalTable: "DownloadFiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
