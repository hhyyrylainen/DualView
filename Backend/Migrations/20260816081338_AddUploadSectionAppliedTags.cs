using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadSectionAppliedTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UploadSectionAppliedTags",
                columns: table => new
                {
                    AppliedTagsId = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadSectionsId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadSectionAppliedTags", x => new { x.AppliedTagsId, x.UploadSectionsId });
                    table.ForeignKey(
                        name: "FK_UploadSectionAppliedTags_AppliedTags_AppliedTagsId",
                        column: x => x.AppliedTagsId,
                        principalTable: "AppliedTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UploadSectionAppliedTags_UploadSections_UploadSectionsId",
                        column: x => x.UploadSectionsId,
                        principalTable: "UploadSections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UploadSectionAppliedTags_UploadSectionsId",
                table: "UploadSectionAppliedTags",
                column: "UploadSectionsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UploadSectionAppliedTags");
        }
    }
}
