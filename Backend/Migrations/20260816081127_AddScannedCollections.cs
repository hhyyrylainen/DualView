using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddScannedCollections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScannedCollections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TargetFolderId = table.Column<long>(type: "INTEGER", nullable: false),
                    UnrecognizedTags = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    ScannerState = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannedCollections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScannedCollections_MediaFolders_TargetFolderId",
                        column: x => x.TargetFolderId,
                        principalTable: "MediaFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FoundMedia",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScannedCollectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    DownloadUrl = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Referrer = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Cookies = table.Column<string>(type: "TEXT", maxLength: 16384, nullable: true),
                    Hash = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LocalThumbnailFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    LocalFullFilePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    UnparsedTags = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoundMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FoundMedia_ScannedCollections_ScannedCollectionId",
                        column: x => x.ScannedCollectionId,
                        principalTable: "ScannedCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScannedCollectionAppliedTags",
                columns: table => new
                {
                    AppliedTagsId = table.Column<long>(type: "INTEGER", nullable: false),
                    ScannedCollectionsId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScannedCollectionAppliedTags", x => new { x.AppliedTagsId, x.ScannedCollectionsId });
                    table.ForeignKey(
                        name: "FK_ScannedCollectionAppliedTags_AppliedTags_AppliedTagsId",
                        column: x => x.AppliedTagsId,
                        principalTable: "AppliedTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScannedCollectionAppliedTags_ScannedCollections_ScannedCollectionsId",
                        column: x => x.ScannedCollectionsId,
                        principalTable: "ScannedCollections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FoundMediaAppliedTags",
                columns: table => new
                {
                    AppliedTagsId = table.Column<long>(type: "INTEGER", nullable: false),
                    FoundMediaId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoundMediaAppliedTags", x => new { x.AppliedTagsId, x.FoundMediaId });
                    table.ForeignKey(
                        name: "FK_FoundMediaAppliedTags_AppliedTags_AppliedTagsId",
                        column: x => x.AppliedTagsId,
                        principalTable: "AppliedTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FoundMediaAppliedTags_FoundMedia_FoundMediaId",
                        column: x => x.FoundMediaId,
                        principalTable: "FoundMedia",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FoundMedia_ScannedCollectionId",
                table: "FoundMedia",
                column: "ScannedCollectionId");

            migrationBuilder.CreateIndex(
                name: "IX_FoundMediaAppliedTags_FoundMediaId",
                table: "FoundMediaAppliedTags",
                column: "FoundMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedCollectionAppliedTags_ScannedCollectionsId",
                table: "ScannedCollectionAppliedTags",
                column: "ScannedCollectionsId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedCollections_Name",
                table: "ScannedCollections",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedCollections_TargetFolderId",
                table: "ScannedCollections",
                column: "TargetFolderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoundMediaAppliedTags");

            migrationBuilder.DropTable(
                name: "ScannedCollectionAppliedTags");

            migrationBuilder.DropTable(
                name: "FoundMedia");

            migrationBuilder.DropTable(
                name: "ScannedCollections");
        }
    }
}
