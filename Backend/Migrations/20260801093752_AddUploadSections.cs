using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadSections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadSections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameLowercase = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    KeepTarget = table.Column<bool>(type: "INTEGER", nullable: false),
                    DisplayIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    LastImported = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Selected = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UploadSectionItems",
                columns: table => new
                {
                    UploadSectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    MediaFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSectionItems", x => new { x.UploadSectionId, x.MediaFileId });
                    table.ForeignKey(
                        name: "FK_UploadSectionItems_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UploadSectionItems_UploadSections_UploadSectionId",
                        column: x => x.UploadSectionId,
                        principalTable: "UploadSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadSectionItems_MediaFileId",
                table: "UploadSectionItems",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadSectionItems_UploadSectionId_Index",
                table: "UploadSectionItems",
                columns: new[] { "UploadSectionId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadSections_NameLowercase",
                table: "UploadSections",
                column: "NameLowercase",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadSections_Selected",
                table: "UploadSections",
                column: "Selected",
                unique: true,
                filter: "[Selected] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadSectionItems");

            migrationBuilder.DropTable(
                name: "UploadSections");
        }
    }
}
