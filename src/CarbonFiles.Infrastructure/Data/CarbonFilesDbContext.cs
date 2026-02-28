using CarbonFiles.Infrastructure.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CarbonFiles.Infrastructure.Data;

public class CarbonFilesDbContext : DbContext
{
    public CarbonFilesDbContext(DbContextOptions<CarbonFilesDbContext> options) : base(options) { }

    public DbSet<BucketEntity> Buckets => Set<BucketEntity>();
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();
    public DbSet<ShortUrlEntity> ShortUrls => Set<ShortUrlEntity>();
    public DbSet<UploadTokenEntity> UploadTokens => Set<UploadTokenEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BucketEntity>(e =>
        {
            e.ToTable("Buckets");
            e.HasKey(b => b.Id);
            e.Property(b => b.Id).HasMaxLength(10);
            e.Property(b => b.Name).IsRequired().HasMaxLength(255);
            e.Property(b => b.Owner).IsRequired().HasMaxLength(255);
            e.Property(b => b.OwnerKeyPrefix).HasMaxLength(13);
            e.Property(b => b.Description).HasMaxLength(1000);
            e.HasIndex(b => b.OwnerKeyPrefix);
            e.HasIndex(b => b.ExpiresAt);
            e.HasIndex(b => b.Owner);
        });

        modelBuilder.Entity<FileEntity>(e =>
        {
            e.ToTable("Files");
            e.HasKey(f => new { f.BucketId, f.Path });
            e.Property(f => f.BucketId).HasMaxLength(10);
            e.Property(f => f.Path).HasMaxLength(1024);
            e.Property(f => f.Name).IsRequired().HasMaxLength(255);
            e.Property(f => f.MimeType).IsRequired().HasMaxLength(255);
            e.Property(f => f.ShortCode).HasMaxLength(6);
            e.HasIndex(f => f.BucketId);
            e.HasIndex(f => f.ShortCode).IsUnique().HasFilter("ShortCode IS NOT NULL");
        });

        modelBuilder.Entity<ApiKeyEntity>(e =>
        {
            e.ToTable("ApiKeys");
            e.HasKey(k => k.Prefix);
            e.Property(k => k.Prefix).HasMaxLength(13);
            e.Property(k => k.HashedSecret).IsRequired().HasMaxLength(64);
            e.Property(k => k.Name).IsRequired().HasMaxLength(255);
        });

        modelBuilder.Entity<ShortUrlEntity>(e =>
        {
            e.ToTable("ShortUrls");
            e.HasKey(s => s.Code);
            e.Property(s => s.Code).HasMaxLength(6);
            e.Property(s => s.BucketId).IsRequired().HasMaxLength(10);
            e.Property(s => s.FilePath).IsRequired().HasMaxLength(1024);
            e.HasIndex(s => new { s.BucketId, s.FilePath });
        });

        modelBuilder.Entity<UploadTokenEntity>(e =>
        {
            e.ToTable("UploadTokens");
            e.HasKey(t => t.Token);
            e.Property(t => t.Token).HasMaxLength(55);
            e.Property(t => t.BucketId).IsRequired().HasMaxLength(10);
            e.HasIndex(t => t.BucketId);
        });
    }
}
