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
    public DbSet<MediaFile> MediaFiles { get; set; }

    public DbSet<Tag> Tags { get; set; }
    public DbSet<TagAlias> TagAliases { get; set; }
    public DbSet<TagModifier> TagModifiers { get; set; }
    public DbSet<TagModifierAlias> TagModifierAliases { get; set; }
    public DbSet<TagImply> TagImplies { get; set; }
    public DbSet<AppliedTag> AppliedTags { get; set; }
    public DbSet<TagBreakRule> TagBreakRules { get; set; }

    public DbSet<MediaImportInfo> MediaImportInfos { get; set; }
    public DbSet<IgnoredDuplicate> IgnoredDuplicates { get; set; }
    public DbSet<DownloadGallery> DownloadGalleries { get; set; }

    // MediaRating, ImageRegion, ActionHistory, and DownloadFile were removed/merged in the new version of DualView

    public static async Task SeedData(DbContext context, CancellationToken cancellation)
    {
        var dbContext = (AppDbContext)context;

        // Create default folders
        var folder =
            await dbContext.MediaFolders.FirstOrDefaultAsync(f => f.Name == "Uncategorized", cancellation);

        if (folder == null)
        {
            folder = new MediaFolder("Uncategorized", null)
            {
                Id = MediaFolder.UncategorizedFolderId,
            };
            await dbContext.MediaFolders.AddAsync(folder, cancellation);
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

            builder.HasMany(d => d.ContainedCollections).WithOne(p => p.Folder)
                .HasForeignKey(p => p.FolderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(d => d.SubFolders).WithOne(p => p.Parent)
                .HasForeignKey(p => p.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
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
            builder.HasOne(d => d.DownloadGallery).WithMany(p => p.AssociatedImports)
                .HasForeignKey(d => d.DownloadGalleryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<IgnoredDuplicate>(builder =>
        {
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

        modelBuilder.Entity<CollectionItem>(builder =>
        {
            builder.HasKey(ci => new { ci.CollectionId, ci.MediaFileId });

            builder.HasOne(ci => ci.Collection)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.CollectionId);

            builder.HasOne(ci => ci.MediaFile)
                .WithMany(m => m.InCollections)
                .HasForeignKey(ci => ci.MediaFileId);
        });
    }
}
