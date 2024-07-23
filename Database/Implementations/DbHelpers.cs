using System.Diagnostics.CodeAnalysis;
using Core.Context;
using Core.Entities;
using Cysharp.Text;
using Dapper;
using Database.Interfaces;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

#pragma warning disable CS8604 // Possible null reference argument.


namespace Database.Implementations;

public class DbHelpers : IDbHelpers
{
    private readonly IDbContextFactory<SimilarityContext> _contextFactory;

    private readonly Utf16PreparedFormat<string, string, string, string, string> _getImagesGroupInfosQuery =
        ZString.PrepareUtf16<string, string, string, string, string>(
            """SELECT {0}, {1} FROM {2} WHERE {3} = '\x{4}'""");

    private readonly Utf16PreparedFormat<string, string, string, string, string, string> _insertImagesGroupQuery =
        ZString.PrepareUtf16<string, string, string, string, string, string>(
            """INSERT INTO {0}({1}, {2}) VALUES ('\x{3}', '{4}') RETURNING {5}""");

    private readonly Utf16PreparedFormat<string, string, string, string, long, string, long>
        _getExisitingSimilaritiesForImagesGroupQuery =
            ZString.PrepareUtf16<string, string, string, string, long, string, long>(
                "SELECT DISTINCT UNNEST(ARRAY[{0}, {1}]) FROM {2} WHERE {3} = {4} OR {5} = {6}");

    private readonly Utf16PreparedFormat<string, string, string, string, long, long, double>
        _insertNewSimilarityQuery =
            ZString.PrepareUtf16<string, string, string, string, long, long, double>(
                "INSERT INTO {0}({1}, {2}, {3}) VALUES ({4}, {5}, {6})");

    // private readonly Utf16PreparedFormat<string, string, string, string, long, string, long>
    //     _similarImagesGroupsQuery =
    //         ZString.PrepareUtf16<string, string, string, string, long, string, long>(
    //             "SELECT {0} {}");

    public DbHelpers(IDbContextFactory<SimilarityContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    // public async Task<(long Id, Vector? ImageHash)> GetImageInfosAsync(byte[] hash,
    //     CancellationToken cancellationToken)
    // {
    //     ImagesGroup? group;
    //     await using (var context = await _contextFactory.CreateDbContextAsync(cancellationToken))
    //     {
    //         group = context.Database.GetDbConnection().QueryFirstOrDefault<ImagesGroup>(
    //             _getImagesGroupInfosQuery.Format(
    //                 context.ImagesGroups.EntityType.GetProperty("Id").GetColumnName(),
    //                 context.ImagesGroups.EntityType.GetProperty("ImageHash").GetColumnName(),
    //                 context.ImagesGroups.EntityType.GetTableName(),
    //                 context.ImagesGroups.EntityType.GetProperty("Hash").GetColumnName(),
    //                 Convert.ToHexString(hash)));
    //     }
    //     return group == null ? (0, null) : (group.Id, group.ImageHash);
    // }

    public async Task<long> CacheHashAsync(ImagesGroup group, CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        // return context.Database.GetDbConnection().ExecuteScalar<long>(_insertImagesGroupQuery.Format(
        //     context.ImagesGroups.EntityType.GetTableName(),
        //     context.ImagesGroups.EntityType.GetProperty("Hash").GetColumnName(),
        //     context.ImagesGroups.EntityType.GetProperty("ImageHash").GetColumnName(),
        //     Convert.ToHexString(group.Hash).ToLowerInvariant(), group.ImageHash.ToString(),
        //     context.ImagesGroups.EntityType.GetProperty("Id").GetColumnName()));
        return 0;
    }

    public async Task<HashSet<long>> GetSimilarImagesGroupsAlreadyDoneInRange(long currentGroupId,
        double degreeOfSimilarity,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);

        return context.Database.GetDbConnection().Query<long>(_getExisitingSimilaritiesForImagesGroupQuery.Format(
            context.Similarities.EntityType.GetProperty("OriginalId").GetColumnName(),
            context.Similarities.EntityType.GetProperty("DuplicateId").GetColumnName(),
            context.Similarities.EntityType.GetTableName(),
            context.Similarities.EntityType.GetProperty("OriginalId").GetColumnName(),
            currentGroupId,
            context.Similarities.EntityType.GetProperty("DuplicateId").GetColumnName(),
            currentGroupId)).ToHashSet();
    }

    [SuppressMessage("ReSharper", "EntityFramework.UnsupportedServerSideFunctionCall")]
    public async Task<List<Similarity>> GetSimilarImagesGroups(long currentGroupId, Vector imageHash,
        double degreeOfSimilarity, ICollection<long> groupsAlreadyDone,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        
        // return await context.ImagesGroups
        //     .Where(similarGroup => !groupsAlreadyDone.Contains(similarGroup.Id) &&
        //                            similarGroup.ImageHash.CosineDistance(imageHash) <= 1 - degreeOfSimilarity)
        //     .Select(similarGroup => new Similarity
        //     {
        //         OriginalId = currentGroupId, DuplicateId = similarGroup.Id,
        //         Score = similarGroup.ImageHash.CosineDistance(imageHash)
        //     })
        //     .ToListAsync(cancellationToken);
        return [];
    }

    public async Task AddSimilarity(Similarity similarity,
        CancellationToken cancellationToken)
    {
        await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
        
        context.Database.GetDbConnection().Execute(_insertNewSimilarityQuery.Format(
            context.Similarities.EntityType.GetTableName(),
            context.Similarities.EntityType.GetProperty("OriginalId").GetColumnName(),
            context.Similarities.EntityType.GetProperty("DuplicateId").GetColumnName(),
            context.Similarities.EntityType.GetProperty("Score").GetColumnName(),
            similarity.OriginalId,
            similarity.DuplicateId,
            similarity.Score));
        
        // context.Database.GetDbConnection().BulkInsert(newSimilarities);
    }
}