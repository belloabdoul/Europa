using System.Text;
using System.Text.Json;
using Core.Entities;
using Database.Interfaces;
using ObservableCollections;
using Redis.OM;
using Redis.OM.Contracts;
using Redis.OM.Searching;

#pragma warning disable CS8604 // Possible null reference argument.


namespace Database.Implementations;

public class DbHelpers : IDbHelpers
{
    private static readonly string[] QueryParts =
    [
        "FT.SEARCH", nameof(ImagesGroup), "PARAMS", "4", "distance", "vector", "SORTBY", "RETURN",
        $"$.{nameof(ImagesGroup.ImageHash)}", "DIALECT", "2"
    ];

    private readonly RedisCollection<ImagesGroup> _imagesGroupsCollection;
    private readonly IRedisConnection _redisConnectionProvider;

    public DbHelpers(RedisConnectionProvider redisConnectionProvider)
    {
        _redisConnectionProvider = redisConnectionProvider.Connection;
        _imagesGroupsCollection =
            (RedisCollection<ImagesGroup>)redisConnectionProvider.RedisCollection<ImagesGroup>(false);
    }

    public async Task<Vector<byte[]>?> GetImageInfosAsync(HashKey id)
    {
        return (await _imagesGroupsCollection.FindByIdAsync($"{nameof(ImagesGroup)}:{id}"))?.ImageHash;
    }

    public async Task CacheHashAsync(ImagesGroup group)
    {
        // Console.WriteLine(group.Id.GetType().IsValueType);
        await _imagesGroupsCollection.InsertAsync(group);
    }

    public async Task<ObservableHashSet<HashKey>> GetSimilarImagesAlreadyDoneInRange(HashKey id)
    {
        return new ObservableHashSet<HashKey>(
            (await _imagesGroupsCollection.FindByIdAsync($"{nameof(ImagesGroup)}:{id}"))!.Similarities
            .Select(similarity => similarity.DuplicateId));
    }

    public async Task<List<Similarity>> GetSimilarImages(HashKey id, Vector<byte[]> imageHash,
        int degreeOfSimilarity, IReadOnlyCollection<HashKey> groupsAlreadyDone)
    {
        var query = new object[16];
        query[0] = QueryParts[1];
        var queryBuilder = new StringBuilder();

        if (groupsAlreadyDone.Count > 0)
        {
            queryBuilder.Append($"-@{nameof(ImagesGroup.Id)}:");
            queryBuilder.Append('{');
            var i = 0;
            foreach (var group in groupsAlreadyDone)
            {
                queryBuilder.Append(group);
                if (i != groupsAlreadyDone.Count - 1)
                    queryBuilder.Append('|');
                i++;
            }

            queryBuilder.Append("} ");
        }

        queryBuilder.Append("@ImageHash:[VECTOR_RANGE $distance $vector]=>{$YIELD_DISTANCE_AS: distance}");

        query[1] = queryBuilder.ToString();

        query[2] = QueryParts[2];
        query[3] = QueryParts[3];
        query[4] = QueryParts[4];
        query[5] = degreeOfSimilarity;
        query[6] = QueryParts[5];
        query[7] = imageHash.Embedding!;
        query[8] = QueryParts[6];
        query[9] = QueryParts[4];
        query[10] = QueryParts[7];
        query[11] = QueryParts[10];
        query[12] = nameof(ImagesGroup.Id);
        query[13] = query[4] = QueryParts[4];
        query[14] = QueryParts[9];
        query[15] = QueryParts[10];

        var result = new SearchResponse(await _redisConnectionProvider.ExecuteAsync(QueryParts[0], query));

        return result.Documents.Values.Select(group => new Similarity
        {
            OriginalId = id, DuplicateId = new HashKey(Convert.FromHexString(group[nameof(ImagesGroup.Id)])),
            Score = Convert.ToDouble(group[QueryParts[4]])
        }).ToList();
    }

    public async Task LinkToSimilarImagesAsync(HashKey id, ICollection<Similarity> newSimilarities, bool isEmpty)
    {
        var query = new object[newSimilarities.Count + 3];
        query[0] = $"{nameof(ImagesGroup)}:{id}";
        query[1] = $"$.{nameof(ImagesGroup.Similarities)}";

        query[2] = isEmpty ? 0 : -1;

        var i = 3;
        foreach (var similarity in newSimilarities)
        {
            query[i] = JsonSerializer.Serialize(similarity);
            i++;
        }

        await _redisConnectionProvider.ExecuteAsync("JSON.ARRINSERT", query);
    }
}