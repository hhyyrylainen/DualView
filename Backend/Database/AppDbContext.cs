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

    public DbSet<MediaStorageFolder> MediaStorageFolders { get; set; }
    public DbSet<ConfiguredMedia> ConfiguredMedia { get; set; }
    public DbSet<MediaFile> MediaFiles { get; set; }

    public static async Task SeedData(DbContext context, CancellationToken cancellation)
    {
        var dbContext = (AppDbContext)context;

        // Create default folders
        var folder =
            await dbContext.MediaStorageFolders.FirstOrDefaultAsync(f => f.Name == "Uncategorized", cancellation);

        if (folder == null)
        {
            folder = new MediaStorageFolder("Uncategorized", null)
            {
                Id = MediaStorageFolder.UncategorizedFolderId,
            };
            await dbContext.MediaStorageFolders.AddAsync(folder, cancellation);
            await dbContext.SaveChangesAsync(cancellation);
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<MediaFile>(builder =>
        {
            builder.HasMany(d => d.Configurations).WithOne(p => p.MediaFile)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(d => d.DerivedFrom).WithMany(p => p.DerivedImages)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<MediaStorageFolder>(builder =>
        {
            builder.HasMany(d => d.ContainedItems).WithMany(p => p.InFolders)
                .UsingEntity(entityBuilder => entityBuilder.ToTable("MediaFolderMedia"));

            builder.HasMany(d => d.SubFolders).WithOne(p => p.Parent).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<ConfiguredMedia>(builder =>
        {
            // Ensure only one prime (unmodified) media per MediaFile
            builder
                .HasIndex(e => e.MediaFileId)
                .HasFilter("Prime = 1") // SQLite: boolean true is 1
                .IsUnique();
        });
    }
}
