using DualView.Shared.Models;
using Backend.Models;

namespace Backend.Database;

using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<DualViewSettings> AppSettings { get; set; }

    public DbSet<MaintenanceJobRecord> MaintenanceJobRecords { get; set; }

    public DbSet<MediaFolder> MediaFolders { get; set; }
    public DbSet<Collection> Collections { get; set; }
    public DbSet<UploadSection> UploadSections { get; set; }
    public DbSet<UploadSectionItem> UploadSectionItems { get; set; }
    public DbSet<MediaFile> MediaFiles { get; set; }

    public DbSet<Tag> Tags { get; set; }
    public DbSet<TagAlias> TagAliases { get; set; }
    public DbSet<TagModifier> TagModifiers { get; set; }
    public DbSet<TagImply> TagImplies { get; set; }
    public DbSet<AppliedTag> AppliedTags { get; set; }
    public DbSet<TagBreakRule> TagBreakRules { get; set; }
    public DbSet<TagSuperAlias> TagSuperAliases { get; set; }

    public DbSet<MediaImportInfo> MediaImportInfos { get; set; }
    public DbSet<IgnoredDuplicate> IgnoredDuplicates { get; set; }
    public DbSet<DownloadGallery> DownloadGalleries { get; set; }

    // MediaRating, ImageRegion, ActionHistory, and DownloadFile were removed/merged in the new version of DualView

    public static async Task SeedData(DbContext context, CancellationToken cancellation)
    {
        var dbContext = (AppDbContext)context;

        // Create default collections and folders
        var folder =
            await dbContext.MediaFolders.Include(f => f.ContainedCollections)
                .FirstOrDefaultAsync(f => f.Id == MediaFolder.RootFolderId, cancellation);

        if (folder == null)
        {
            folder = new MediaFolder("Root")
            {
                Id = MediaFolder.RootFolderId,
            };
            await dbContext.MediaFolders.AddAsync(folder, cancellation);
            await dbContext.SaveChangesAsync(cancellation);
        }
        else if (folder.Name != "Root")
        {
            folder.Name = "Root";
            folder.NameLowerCase = "root";
            await dbContext.SaveChangesAsync(cancellation);
        }

        var collection =
            await dbContext.Collections.FirstOrDefaultAsync(c => c.Id == Collection.UncategorizedCollectionId, cancellation);

        if (collection == null)
        {
            collection = new Collection("Uncategorized")
            {
                Id = Collection.UncategorizedCollectionId,
            };
            await dbContext.Collections.AddAsync(collection, cancellation);
            await dbContext.SaveChangesAsync(cancellation);
        }

        // Ensure the uncategorized collection is in the root folder.
        // It is a bit less efficient to do it this way around but should be good enough (root folder shouldn't be that full).
        if (folder.ContainedCollections.All(c => c.Id != Collection.UncategorizedCollectionId))
        {
            folder.ContainedCollections.Add(collection);
            await dbContext.SaveChangesAsync(cancellation);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaFile>(builder =>
        {
            builder.HasQueryFilter(m => !m.IsDeleted);

            builder.HasOne(d => d.ParentMedia).WithMany(p => p.Children)
                .HasForeignKey(d => d.ParentMediaId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasMany(d => d.AppliedTags).WithMany(p => p.MediaFiles)
                .UsingEntity(j => j.ToTable("MediaFileAppliedTags"));

            builder.HasOne(d => d.ImportInfo).WithOne(p => p.MediaFile)
                .HasForeignKey<MediaImportInfo>(p => p.MediaFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<MediaFolder>(builder =>
        {
            builder.HasQueryFilter(m => !m.IsDeleted);

            builder.HasMany(d => d.ContainedCollections).WithMany(p => p.Folders)
                .UsingEntity(j => j.ToTable("MediaFolderCollections"));

            builder.HasMany(d => d.SubFolders).WithMany(p => p.Parents)
                .UsingEntity(j => j.ToTable("MediaFolderSubFolders"));
        });

        modelBuilder.Entity<Collection>(builder =>
        {
            builder.HasQueryFilter(m => !m.IsDeleted);

            builder.HasMany(d => d.AppliedTags).WithMany(p => p.Collections)
                .UsingEntity(j => j.ToTable("CollectionAppliedTags"));
        });

        modelBuilder.Entity<Tag>(builder =>
        {
            builder.HasQueryFilter(m => !m.IsDeleted);

            builder.HasOne(d => d.ExampleMedia).WithMany()
                .HasForeignKey(d => d.ExampleMediaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<TagImply>(builder =>
        {
            builder.HasQueryFilter(ti => !ti.PrimaryTag.IsDeleted && !ti.ToApplyTag.IsDeleted);
            builder.HasKey(ti => new { ti.PrimaryTagId, ti.ToApplyTagId });

            builder.HasOne(ti => ti.PrimaryTag)
                .WithMany(t => t.Implies)
                .HasForeignKey(ti => ti.PrimaryTagId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(ti => ti.ToApplyTag)
                .WithMany(t => t.ImpliedBy)
                .HasForeignKey(ti => ti.ToApplyTagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AppliedTag>(builder =>
        {
            builder.HasQueryFilter(at => !at.Tag.IsDeleted);
            builder.HasOne(d => d.Tag).WithMany()
                .HasForeignKey(d => d.TagId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(d => d.CombinedWith).WithMany()
                .HasForeignKey(d => d.CombinedWithId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasMany(d => d.Modifiers).WithMany()
                .UsingEntity(j => j.ToTable("AppliedTagModifiers"));
        });

        modelBuilder.Entity<TagModifier>(builder =>
        {
            builder.HasQueryFilter(m => !m.IsDeleted);
        });

        modelBuilder.Entity<TagBreakRule>(builder =>
        {
            builder.HasQueryFilter(m => !m.IsDeleted);

            builder.HasOne(d => d.ActualTag).WithMany()
                .HasForeignKey(d => d.ActualTagId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasMany(d => d.Modifiers).WithMany()
                .UsingEntity(j => j.ToTable("TagBreakRuleModifiers"));
        });

        modelBuilder.Entity<MediaImportInfo>(builder =>
        {
            builder.HasQueryFilter(mii => !mii.MediaFile.IsDeleted);
            builder.HasOne(d => d.DownloadGallery).WithMany(p => p.AssociatedImports)
                .HasForeignKey(d => d.DownloadGalleryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IgnoredDuplicate>(builder =>
        {
            builder.HasQueryFilter(id => !id.MediaFile1.IsDeleted && !id.MediaFile2.IsDeleted);
            builder.HasKey(id => new { id.MediaFileId1, id.MediaFileId2 });

            builder.HasOne(id => id.MediaFile1).WithMany()
                .HasForeignKey(id => id.MediaFileId1)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(id => id.MediaFile2).WithMany()
                .HasForeignKey(id => id.MediaFileId2)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DownloadGallery>(builder =>
        {
            builder.HasQueryFilter(m => !m.IsDeleted);
        });

        modelBuilder.Entity<UploadSection>(builder =>
        {
            builder.HasIndex(s => s.Selected)
                .IsUnique()
                .HasFilter("[Selected] = 1");

            builder.HasOne<MediaFolder>()
                .WithMany()
                .HasForeignKey(s => s.TargetFolderId)
                .OnDelete(DeleteBehavior.Restrict);

        });

        modelBuilder.Entity<UploadSectionItem>(builder =>
        {
            builder.HasQueryFilter(usi => !usi.MediaFile.IsDeleted);
            builder.HasKey(ci => new { ci.UploadSectionId, ci.MediaFileId });

            builder.HasOne(ci => ci.UploadSection)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.UploadSectionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(ci => ci.MediaFile)
                .WithMany(m => m.InUploadSections)
                .HasForeignKey(ci => ci.MediaFileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CollectionItem>(builder =>
        {
            builder.HasQueryFilter(ci => !ci.Collection.IsDeleted && !ci.MediaFile.IsDeleted);
            builder.HasKey(ci => new { ci.CollectionId, ci.MediaFileId });

            builder.HasOne(ci => ci.Collection)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.CollectionId);

            builder.HasOne(ci => ci.MediaFile)
                .WithMany(m => m.InCollections)
                .HasForeignKey(ci => ci.MediaFileId);
        });

        modelBuilder.Entity<TagAlias>(builder =>
        {
            builder.HasQueryFilter(ta => !ta.Tag.IsDeleted);
        });
    }
}
