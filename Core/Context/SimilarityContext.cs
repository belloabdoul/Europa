using Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Core.Context;

public class SimilarityContext : DbContext
{
    public SimilarityContext(DbContextOptions<SimilarityContext> options) : base(options)
    {
    }

    public DbSet<ImagesGroup> ImagesGroups { get; init; }
    public DbSet<Similarity> Similarities { get; init; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

        // Table "files"
        modelBuilder.Entity<ImagesGroup>(
            entityBuilder =>
            {
                // Properties
                entityBuilder.Property(group => group.Id).HasColumnName("id").UseIdentityByDefaultColumn()
                    .HasIdentityOptions(startValue: 1);
                // entityBuilder.Property(group => group.Hash).HasColumnName("hash")
                //     .HasConversion(
                //         hash => hash,
                //         hash => hash.Select(byteValue => Utilities.ByteToByte[byteValue]).ToArray(),
                //         new ValueComparer<byte[]>(
                //             (c1, c2) => (c1 == null && c2 == null) ||
                //                         (c1 != null && c2 != null && c1.SequenceEqual(c2)),
                //             c => string.GetHashCode(Convert.ToHexString(c), StringComparison.OrdinalIgnoreCase),
                //             c => c))
                //     .HasMaxLength(32);

                entityBuilder.Property(group => group.ImageHash).HasColumnName("image_hash").HasColumnType("vector(32)");

                // Ignore
                entityBuilder.Ignore(group => group.DateModified);
                entityBuilder.Ignore(group => group.Size);
                entityBuilder.Ignore(group => group.Duplicates);
                entityBuilder.Ignore(group => group.SimilarImagesGroups);

                // Keys and indexes
                entityBuilder.HasKey(group => group.Id);
                entityBuilder.HasIndex(group => group.Hash);
                entityBuilder.HasIndex(group => group.ImageHash)
                    .IsCreatedConcurrently()
                    .HasMethod("hnsw")
                    .HasOperators("vector_cosine_ops")
                    .HasStorageParameter("m", 16)
                    .HasStorageParameter("ef_construction", 200);

                modelBuilder.Entity<ImagesGroup>().ToTable("images_groups");
            });

        // Table "similarities"
        modelBuilder.Entity<Similarity>(
            entityBuilder =>
            {
                // Properties
                entityBuilder.Property(similarity => similarity.OriginalId).HasColumnName("original_id");
                entityBuilder.Property(similarity => similarity.DuplicateId).HasColumnName("duplicate_id");
                entityBuilder.Property(similarity => similarity.Score).HasColumnName("score");

                // Ignore
                entityBuilder.Ignore(similarity => similarity.Original);
                entityBuilder.Ignore(similarity => similarity.Duplicate);

                // Keys and relationships
                entityBuilder.HasKey(similarity => new {similarity.OriginalId, similarity.DuplicateId});
                entityBuilder
                    .HasOne(e => e.Original)
                    .WithMany()
                    .HasForeignKey(e => e.OriginalId);
                entityBuilder
                    .HasOne(e => e.Duplicate)
                    .WithMany()
                    .HasForeignKey(e => e.DuplicateId);

                entityBuilder.ToTable("similarities");
            });
    }
}