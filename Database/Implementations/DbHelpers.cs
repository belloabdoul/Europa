using Core.Entities;
using Database.Interfaces;
using File = Core.Entities.File;
using Core.Context;
using Microsoft.EntityFrameworkCore;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using EFCore.BulkExtensions;
using Pgvector.EntityFrameworkCore;
using Pgvector;

#pragma warning disable CS8604 // Possible null reference argument.

namespace Database.Implementations
{
    public class DbHelpers : IDbHelpers
    {
        private readonly IDbContextFactory<SimilarityContext> _contextFactory;

        public DbHelpers(IDbContextFactory<SimilarityContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task GetCachedHashAsync(ConcurrentQueue<File> filesWithoutImageHash,
            CancellationToken cancellationToken)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var bulkConfig = new BulkConfig { UpdateByProperties = [nameof(File.Id)] };
            await context.BulkReadAsync(filesWithoutImageHash, bulkConfig, cancellationToken: cancellationToken);
        }

        public async Task<bool> CacheHashAsync(ConcurrentQueue<File> filesToCache, CancellationToken cancellationToken)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
            await context.BulkInsertAsync(filesToCache, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return true;
        }

        public async Task<List<string>> GetSimilarImagesGroupsAssociatedToAsync(string currentFile,
            CancellationToken cancellationToken)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            return await context.Similarities.Where(similarity => similarity.HypotheticalOriginalId.Equals(currentFile))
                .Select(similarity => similarity.HypotheticalDuplicateId).ToListAsync(cancellationToken);
        }

        [SuppressMessage("ReSharper", "EntityFramework.UnsupportedServerSideFunctionCall")]
        public async Task<List<Similarity>> GetSimilarImagesGroups(Vector imageHash, double threshold,
            List<string> imagesGroupsAlreadyDone, CancellationToken cancellationToken)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            if (imagesGroupsAlreadyDone.Count == 0)
                return await context.Files
                    .Where(f => f.ImageHash.CosineDistance(imageHash) <= 1 - threshold)
                    .Select(f => new Similarity
                    {
                        HypotheticalOriginalId = string.Empty, HypotheticalDuplicateId = f.Id,
                        Score = f.ImageHash.CosineDistance(imageHash)
                    })
                    .ToListAsync(cancellationToken);
            return await context.Files
                .Where(f => !imagesGroupsAlreadyDone.Contains(f.Id) &&
                            f.ImageHash.CosineDistance(imageHash) <= 1 - threshold)
                .Select(f => new Similarity
                {
                    HypotheticalOriginalId = string.Empty, HypotheticalDuplicateId = f.Id,
                    Score = f.ImageHash.CosineDistance(imageHash)
                })
                .ToListAsync(cancellationToken);
        }

        public async Task SetSimilaritiesAsync(IEnumerable<Similarity> similarities,
            CancellationToken cancellationToken)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            await context.BulkInsertAsync(similarities, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        public async Task<List<string>> GetSimilarImagesGroupsInRangeAsync(string currentFile, double threshold,
            CancellationToken cancellationToken)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

            return await context.Similarities.Where(similarity =>
                    similarity.HypotheticalOriginalId.Equals(currentFile) && similarity.Score <= 1 - threshold)
                .Select(similarity => similarity.HypotheticalDuplicateId).ToListAsync(cancellationToken);
        }
    }
}