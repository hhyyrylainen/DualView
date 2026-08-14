using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations;

public partial class ReworkUploadSectionsFinal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_UploadSections_NameLowercase", "UploadSections");

        migrationBuilder.AddColumn<bool>("RemoveAfterImport", "UploadSections", type: "INTEGER",
            nullable: false, defaultValue: true);
        migrationBuilder.AddColumn<string>("TargetCollectionName", "UploadSections", type: "TEXT",
            maxLength: 200, nullable: false, defaultValue: "");
        migrationBuilder.AddColumn<long>("TargetFolderId", "UploadSections", type: "INTEGER",
            nullable: false, defaultValue: 1L);

        migrationBuilder.CreateIndex("IX_UploadSections_NameLowercase", "UploadSections", "NameLowercase");
        migrationBuilder.CreateIndex("IX_UploadSections_TargetFolderId", "UploadSections", "TargetFolderId");
        migrationBuilder.AddForeignKey("FK_UploadSections_MediaFolders_TargetFolderId", "UploadSections",
            "TargetFolderId", "MediaFolders", "Id", onDelete: ReferentialAction.Restrict);
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
