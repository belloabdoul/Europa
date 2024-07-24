using Core.Entities;
using Database.Interfaces;

#pragma warning disable CS8604 // Possible null reference argument.


namespace Database.Implementations;

public class DbHelpers : IDbHelpers
{
    public DbHelpers()
    {
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
        return 0;
    }

    public async Task<HashSet<long>> GetSimilarImagesGroupsAlreadyDoneInRange(long currentGroupId,
        double degreeOfSimilarity,
        CancellationToken cancellationToken)
    {
        return [];
    }

    // [SuppressMessage("ReSharper", "EntityFramework.UnsupportedServerSideFunctionCall")]
    // public async Task<List<Similarity>> GetSimilarImagesGroups(long currentGroupId, Vector imageHash,
    //     double degreeOfSimilarity, ICollection<long> groupsAlreadyDone,
    //     CancellationToken cancellationToken)
    // {
    // }

    public async Task AddSimilarity(Similarity similarity,
        CancellationToken cancellationToken)
    {
    }
}