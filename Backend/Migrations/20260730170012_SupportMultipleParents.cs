using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class SupportMultipleParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Collections_MediaFolders_FolderId",
                table: "Collections");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaFolders_MediaFolders_ParentId",
                table: "MediaFolders");

            migrationBuilder.DropIndex(
                name: "IX_MediaFolders_ParentId_NameLowerCase",
                table: "MediaFolders");

            migrationBuilder.DropIndex(
                name: "IX_Collections_FolderId",
                table: "Collections");

            migrationBuilder.DropColumn(
                name: "ParentId",
                table: "MediaFolders");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "Collections");

            migrationBuilder.CreateTable(
                name: "MediaFolderCollections",
                columns: table => new
                {
                    ContainedCollectionsId = table.Column<long>(type: "INTEGER", nullable: false),
                    FoldersId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFolderCollections", x => new { x.ContainedCollectionsId, x.FoldersId });
                    table.ForeignKey(
                        name: "FK_MediaFolderCollections_Collections_ContainedCollectionsId",
                        column: x => x.ContainedCollectionsId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaFolderCollections_MediaFolders_FoldersId",
                        column: x => x.FoldersId,
                        principalTable: "MediaFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaFolderSubFolders",
                columns: table => new
                {
                    ParentsId = table.Column<long>(type: "INTEGER", nullable: false),
                    SubFoldersId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFolderSubFolders", x => new { x.ParentsId, x.SubFoldersId });
                    table.ForeignKey(
                        name: "FK_MediaFolderSubFolders_MediaFolders_ParentsId",
                        column: x => x.ParentsId,
                        principalTable: "MediaFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaFolderSubFolders_MediaFolders_SubFoldersId",
                        column: x => x.SubFoldersId,
                        principalTable: "MediaFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolderCollections_FoldersId",
                table: "MediaFolderCollections",
                column: "FoldersId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolderSubFolders_SubFoldersId",
                table: "MediaFolderSubFolders",
                column: "SubFoldersId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MediaFolderCollections");

            migrationBuilder.DropTable(
                name: "MediaFolderSubFolders");

            migrationBuilder.AddColumn<long>(
                name: "ParentId",
                table: "MediaFolders",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FolderId",
                table: "Collections",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_ParentId_NameLowerCase",
                table: "MediaFolders",
                columns: new[] { "ParentId", "NameLowerCase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Collections_FolderId",
                table: "Collections",
                column: "FolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Collections_MediaFolders_FolderId",
                table: "Collections",
                column: "FolderId",
                principalTable: "MediaFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaFolders_MediaFolders_ParentId",
                table: "MediaFolders",
                column: "ParentId",
                principalTable: "MediaFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
