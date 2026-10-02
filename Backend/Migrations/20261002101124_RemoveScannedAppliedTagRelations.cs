using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class RemoveScannedAppliedTagRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoundMediaAppliedTags");

            migrationBuilder.DropTable(
                name: "ScannedCollectionAppliedTags");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_FoundMediaAppliedTags_FoundMediaId",
                table: "FoundMediaAppliedTags",
                column: "FoundMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_ScannedCollectionAppliedTags_ScannedCollectionsId",
                table: "ScannedCollectionAppliedTags",
                column: "ScannedCollectionsId");
        }
    }
}
