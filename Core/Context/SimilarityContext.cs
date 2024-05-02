using Core.Entities;
using Microsoft.EntityFrameworkCore;
using File = Core.Entities.File;

namespace Core.Context
{
    public class SimilarityContext : DbContext
    {
        public DbSet<File> Files { get; init; }
        public DbSet<Similarity> Similarities { get; init; }

        public SimilarityContext(DbContextOptions<SimilarityContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.HasPostgresExtension("vector");

            modelBuilder.Entity<Similarity>()
                .HasKey(s => new { s.HypotheticalOriginalId, s.HypotheticalDuplicateId });

            modelBuilder.Entity<File>()
                .HasMany(u => u.HypotheticalDuplicates)
                .WithOne(f => f.HypotheticalOriginal)
                .HasForeignKey(f => f.HypotheticalOriginalId);

            modelBuilder.Entity<File>()
                .HasMany(u => u.HypotheticalOriginals)
                .WithOne(f => f.HypotheticalDuplicate)
                .HasForeignKey(f => f.HypotheticalDuplicateId);
        }
    }
}