using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Backend.Migrations
{
    /// <inheritdoc />
    public partial class InitialPort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActionHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    Performed = table.Column<bool>(type: "INTEGER", nullable: false),
                    JsonData = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActionHistories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LocalMediaStorageLocation = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    GUIStartupCommand = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AIRunManagerUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AIRunManagerAccessKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AudioBufferingMs = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadGalleries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GalleryUrl = table.Column<string>(type: "TEXT", nullable: false),
                    TargetPath = table.Column<string>(type: "TEXT", nullable: true),
                    GalleryName = table.Column<string>(type: "TEXT", nullable: true),
                    CurrentlyScannedUrl = table.Column<string>(type: "TEXT", nullable: true),
                    IsDownloaded = table.Column<bool>(type: "INTEGER", nullable: false),
                    TagsString = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadGalleries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceJobRecords",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    LastPerformed = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NextRunAfter = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Failed = table.Column<bool>(type: "INTEGER", nullable: false),
                    StatusMessage = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceJobRecords", x => x.Name);
                });

            migrationBuilder.CreateTable(
                name: "MediaFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OriginalFileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameLowerCase = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    HashSha3 = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastViewed = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    Keep = table.Column<bool>(type: "INTEGER", nullable: false),
                    MediaType = table.Column<int>(type: "INTEGER", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    FrameCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FramesPerSecond = table.Column<float>(type: "REAL", nullable: false),
                    CropLeft = table.Column<int>(type: "INTEGER", nullable: false),
                    CropTop = table.Column<int>(type: "INTEGER", nullable: false),
                    CropRight = table.Column<int>(type: "INTEGER", nullable: false),
                    CropBottom = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentMediaId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaFiles_MediaFiles_ParentMediaId",
                        column: x => x.ParentMediaId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaFolders",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameLowerCase = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    ParentId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaFolders_MediaFolders_ParentId",
                        column: x => x.ParentId,
                        principalTable: "MediaFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TagModifiers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagModifiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DownloadFiles",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FileUrl = table.Column<string>(type: "TEXT", nullable: false),
                    PageReferrer = table.Column<string>(type: "TEXT", nullable: true),
                    PreferredName = table.Column<string>(type: "TEXT", nullable: false),
                    TagsString = table.Column<string>(type: "TEXT", nullable: true),
                    DownloadGalleryId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                name: "IgnoredDuplicates",
                columns: table => new
                {
                    MediaFileId1 = table.Column<long>(type: "INTEGER", nullable: false),
                    MediaFileId2 = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IgnoredDuplicates", x => new { x.MediaFileId1, x.MediaFileId2 });
                    table.ForeignKey(
                        name: "FK_IgnoredDuplicates_MediaFiles_MediaFileId1",
                        column: x => x.MediaFileId1,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_IgnoredDuplicates_MediaFiles_MediaFileId2",
                        column: x => x.MediaFileId2,
                        principalTable: "MediaFiles",
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
                    Top = table.Column<int>(type: "INTEGER", nullable: false),
                    Left = table.Column<int>(type: "INTEGER", nullable: false),
                    Right = table.Column<int>(type: "INTEGER", nullable: false),
                    Bottom = table.Column<int>(type: "INTEGER", nullable: false),
                    StartFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    EndFrame = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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
                    IsFavorited = table.Column<bool>(type: "INTEGER", nullable: false),
                    Stars = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    ExampleMediaId = table.Column<long>(type: "INTEGER", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_MediaFiles_ExampleMediaId",
                        column: x => x.ExampleMediaId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Collections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NameLowerCase = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    PreviewMediaId = table.Column<long>(type: "INTEGER", nullable: true),
                    LastViewed = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    FolderId = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Collections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Collections_MediaFolders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "MediaFolders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TagModifierAliases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ModifierId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagModifierAliases", x => x.Name);
                    table.ForeignKey(
                        name: "FK_TagModifierAliases_TagModifiers_ModifierId",
                        column: x => x.ModifierId,
                        principalTable: "TagModifiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaImportInfos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: true),
                    ImportDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Referrer = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DownloadFileId = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaImportInfos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaImportInfos_DownloadFiles_DownloadFileId",
                        column: x => x.DownloadFileId,
                        principalTable: "DownloadFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MediaImportInfos_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppliedTags",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TagId = table.Column<long>(type: "INTEGER", nullable: false),
                    CombinedWithId = table.Column<long>(type: "INTEGER", nullable: true),
                    CombineWord = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppliedTags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AppliedTags_AppliedTags_CombinedWithId",
                        column: x => x.CombinedWithId,
                        principalTable: "AppliedTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AppliedTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagAliases",
                columns: table => new
                {
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    TagId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagAliases", x => x.Name);
                    table.ForeignKey(
                        name: "FK_TagAliases_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagBreakRules",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TagString = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ActualTagId = table.Column<long>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagBreakRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TagBreakRules_Tags_ActualTagId",
                        column: x => x.ActualTagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagImplies",
                columns: table => new
                {
                    PrimaryTagId = table.Column<long>(type: "INTEGER", nullable: false),
                    ToApplyTagId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagImplies", x => new { x.PrimaryTagId, x.ToApplyTagId });
                    table.ForeignKey(
                        name: "FK_TagImplies_Tags_PrimaryTagId",
                        column: x => x.PrimaryTagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagImplies_Tags_ToApplyTagId",
                        column: x => x.ToApplyTagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionItem",
                columns: table => new
                {
                    CollectionId = table.Column<long>(type: "INTEGER", nullable: false),
                    MediaFileId = table.Column<long>(type: "INTEGER", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionItem", x => new { x.CollectionId, x.MediaFileId });
                    table.ForeignKey(
                        name: "FK_CollectionItem_Collections_CollectionId",
                        column: x => x.CollectionId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionItem_MediaFiles_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AppliedTagModifiers",
                columns: table => new
                {
                    AppliedTagId = table.Column<long>(type: "INTEGER", nullable: false),
                    ModifiersId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppliedTagModifiers", x => new { x.AppliedTagId, x.ModifiersId });
                    table.ForeignKey(
                        name: "FK_AppliedTagModifiers_AppliedTags_AppliedTagId",
                        column: x => x.AppliedTagId,
                        principalTable: "AppliedTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AppliedTagModifiers_TagModifiers_ModifiersId",
                        column: x => x.ModifiersId,
                        principalTable: "TagModifiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CollectionAppliedTags",
                columns: table => new
                {
                    AppliedTagsId = table.Column<long>(type: "INTEGER", nullable: false),
                    CollectionsId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CollectionAppliedTags", x => new { x.AppliedTagsId, x.CollectionsId });
                    table.ForeignKey(
                        name: "FK_CollectionAppliedTags_AppliedTags_AppliedTagsId",
                        column: x => x.AppliedTagsId,
                        principalTable: "AppliedTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CollectionAppliedTags_Collections_CollectionsId",
                        column: x => x.CollectionsId,
                        principalTable: "Collections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaFileAppliedTags",
                columns: table => new
                {
                    AppliedTagsId = table.Column<long>(type: "INTEGER", nullable: false),
                    MediaFilesId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFileAppliedTags", x => new { x.AppliedTagsId, x.MediaFilesId });
                    table.ForeignKey(
                        name: "FK_MediaFileAppliedTags_AppliedTags_AppliedTagsId",
                        column: x => x.AppliedTagsId,
                        principalTable: "AppliedTags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MediaFileAppliedTags_MediaFiles_MediaFilesId",
                        column: x => x.MediaFilesId,
                        principalTable: "MediaFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TagBreakRuleModifiers",
                columns: table => new
                {
                    ModifiersId = table.Column<long>(type: "INTEGER", nullable: false),
                    TagBreakRuleId = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TagBreakRuleModifiers", x => new { x.ModifiersId, x.TagBreakRuleId });
                    table.ForeignKey(
                        name: "FK_TagBreakRuleModifiers_TagBreakRules_TagBreakRuleId",
                        column: x => x.TagBreakRuleId,
                        principalTable: "TagBreakRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TagBreakRuleModifiers_TagModifiers_ModifiersId",
                        column: x => x.ModifiersId,
                        principalTable: "TagModifiers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppliedTagModifiers_ModifiersId",
                table: "AppliedTagModifiers",
                column: "ModifiersId");

            migrationBuilder.CreateIndex(
                name: "IX_AppliedTags_CombinedWithId",
                table: "AppliedTags",
                column: "CombinedWithId");

            migrationBuilder.CreateIndex(
                name: "IX_AppliedTags_TagId",
                table: "AppliedTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionAppliedTags_CollectionsId",
                table: "CollectionAppliedTags",
                column: "CollectionsId");

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItem_CollectionId_SequenceNumber",
                table: "CollectionItem",
                columns: new[] { "CollectionId", "SequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CollectionItem_MediaFileId",
                table: "CollectionItem",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_FolderId",
                table: "Collections",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_Collections_NameLowerCase",
                table: "Collections",
                column: "NameLowerCase",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DownloadFiles_DownloadGalleryId",
                table: "DownloadFiles",
                column: "DownloadGalleryId");

            migrationBuilder.CreateIndex(
                name: "IX_IgnoredDuplicates_MediaFileId2",
                table: "IgnoredDuplicates",
                column: "MediaFileId2");

            migrationBuilder.CreateIndex(
                name: "IX_ImageRegions_MediaFileId",
                table: "ImageRegions",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFileAppliedTags_MediaFilesId",
                table: "MediaFileAppliedTags",
                column: "MediaFilesId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_HashSha3",
                table: "MediaFiles",
                column: "HashSha3",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaFiles_ParentMediaId",
                table: "MediaFiles",
                column: "ParentMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFolders_ParentId_NameLowerCase",
                table: "MediaFolders",
                columns: new[] { "ParentId", "NameLowerCase" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaImportInfos_DownloadFileId",
                table: "MediaImportInfos",
                column: "DownloadFileId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaImportInfos_MediaFileId",
                table: "MediaImportInfos",
                column: "MediaFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaRatings_MediaFileId",
                table: "MediaRatings",
                column: "MediaFileId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagAliases_Name",
                table: "TagAliases",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagAliases_TagId",
                table: "TagAliases",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagBreakRuleModifiers_TagBreakRuleId",
                table: "TagBreakRuleModifiers",
                column: "TagBreakRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_TagBreakRules_ActualTagId",
                table: "TagBreakRules",
                column: "ActualTagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagBreakRules_TagString",
                table: "TagBreakRules",
                column: "TagString",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagImplies_ToApplyTagId",
                table: "TagImplies",
                column: "ToApplyTagId");

            migrationBuilder.CreateIndex(
                name: "IX_TagModifierAliases_ModifierId",
                table: "TagModifierAliases",
                column: "ModifierId");

            migrationBuilder.CreateIndex(
                name: "IX_TagModifierAliases_Name",
                table: "TagModifierAliases",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TagModifiers_Name",
                table: "TagModifiers",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_ExampleMediaId",
                table: "Tags",
                column: "ExampleMediaId");

            migrationBuilder.CreateIndex(
                name: "IX_Tags_Name",
                table: "Tags",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActionHistories");

            migrationBuilder.DropTable(
                name: "AppliedTagModifiers");

            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "CollectionAppliedTags");

            migrationBuilder.DropTable(
                name: "CollectionItem");

            migrationBuilder.DropTable(
                name: "IgnoredDuplicates");

            migrationBuilder.DropTable(
                name: "ImageRegions");

            migrationBuilder.DropTable(
                name: "MaintenanceJobRecords");

            migrationBuilder.DropTable(
                name: "MediaFileAppliedTags");

            migrationBuilder.DropTable(
                name: "MediaImportInfos");

            migrationBuilder.DropTable(
                name: "MediaRatings");

            migrationBuilder.DropTable(
                name: "TagAliases");

            migrationBuilder.DropTable(
                name: "TagBreakRuleModifiers");

            migrationBuilder.DropTable(
                name: "TagImplies");

            migrationBuilder.DropTable(
                name: "TagModifierAliases");

            migrationBuilder.DropTable(
                name: "Collections");

            migrationBuilder.DropTable(
                name: "AppliedTags");

            migrationBuilder.DropTable(
                name: "DownloadFiles");

            migrationBuilder.DropTable(
                name: "TagBreakRules");

            migrationBuilder.DropTable(
                name: "TagModifiers");

            migrationBuilder.DropTable(
                name: "MediaFolders");

            migrationBuilder.DropTable(
                name: "DownloadGalleries");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "MediaFiles");
        }
    }
}
