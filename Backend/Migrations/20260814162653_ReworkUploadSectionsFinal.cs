using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations;

public partial class ReworkUploadSectionsFinal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "UploadSectionItems");
        migrationBuilder.DropTable(name: "UploadSections");

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
                TargetCollectionName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                TargetFolderId = table.Column<long>(type: "INTEGER", nullable: false),
                RemoveAfterImport = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UploadSections", x => x.Id);
                table.ForeignKey("FK_UploadSections_MediaFolders_TargetFolderId", x => x.TargetFolderId,
                    "MediaFolders", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "UploadSectionItems",
            columns: table => new
            {
                UploadSectionId = table.Column<long>(type: "INTEGER", nullable: false),
                MediaFileId = table.Column<long>(type: "INTEGER", nullable: false),
                Index = table.Column<int>(type: "INTEGER", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_UploadSectionItems", x => new { x.UploadSectionId, x.MediaFileId });
                table.ForeignKey("FK_UploadSectionItems_MediaFiles_MediaFileId", x => x.MediaFileId,
                    "MediaFiles", "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey("FK_UploadSectionItems_UploadSections_UploadSectionId", x => x.UploadSectionId,
                    "UploadSections", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_UploadSections_NameLowercase", "UploadSections", "NameLowercase");
        migrationBuilder.CreateIndex("IX_UploadSections_Selected", "UploadSections", "Selected", unique: true,
            filter: "[Selected] = 1");
        migrationBuilder.CreateIndex("IX_UploadSections_DisplayIndex", "UploadSections", "DisplayIndex", unique: true);
        migrationBuilder.CreateIndex("IX_UploadSections_TargetFolderId", "UploadSections", "TargetFolderId");
        migrationBuilder.CreateIndex("IX_UploadSectionItems_MediaFileId", "UploadSectionItems", "MediaFileId");
        migrationBuilder.CreateIndex("IX_UploadSectionItems_UploadSectionId_Index", "UploadSectionItems",
            new[] { "UploadSectionId", "Index" }, unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey("FK_UploadSections_MediaFolders_TargetFolderId", "UploadSections");
        migrationBuilder.DropIndex("IX_UploadSections_NameLowercase", "UploadSections");
        migrationBuilder.DropIndex("IX_UploadSections_TargetFolderId", "UploadSections");
        migrationBuilder.DropColumn("RemoveAfterImport", "UploadSections");
        migrationBuilder.DropColumn("TargetCollectionName", "UploadSections");
        migrationBuilder.DropColumn("TargetFolderId", "UploadSections");
        migrationBuilder.CreateIndex("IX_UploadSections_NameLowercase", "UploadSections", "NameLowercase",
            unique: true);
    }
}
