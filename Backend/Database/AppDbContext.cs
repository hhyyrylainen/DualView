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
            builder.HasOne(d => d.ParentMedia).WithMany(p => p.Children)
                .HasForeignKey(d => d.ParentMediaId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaFolder>(builder =>
        {
            builder.HasMany(d => d.ContainedCollections).WithOne(p => p.Folder)
                .HasForeignKey(p => p.FolderId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasMany(d => d.SubFolders).WithOne(p => p.Parent)
                .HasForeignKey(p => p.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
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
