using System.Data;
using System.Runtime.InteropServices;
using System.Text.Json;
using Api.Client.Repositories;
using Core.Entities.Images;
using Core.Entities.Commons;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using Swordfish.NET.Collections;

namespace Api.Client;

public sealed class PgSqlImagesRepository : ICollectionRepository, IImageInfosRepository, IIndexingRepository,
    ISimilarImagesRepository, IDisposable, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    private static readonly HashComparer HashComparer = new();

    private bool _disposed;

    private static readonly string ImageIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.Id));

    private static readonly string ImageHashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.ImageHash));

    private static readonly string ImageSimilaritiesColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.Similarities));

    private static readonly string SimilarityScoreColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.Score));

    public PgSqlImagesRepository(IConnectionStringBuilder connectionStringBuilder)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString);
        dataSourceBuilder.UseVector();
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();
    }

    private static string ConvertNameToSnakeCaseLower(string name)
    {
        var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
        var tableNameSpan = MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference<char>(tableName), name.Length);
        tableNameSpan.Replace('-', '_');
        return tableName;
    }

    public async ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            // Create a table of fingerprints and its index
            await using var command = connection.CreateCommand();

            command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
            await command.ExecuteNonQueryAsync(cancellationToken);
            await connection.ReloadTypesAsync(cancellationToken);

            var tableName = ConvertNameToSnakeCaseLower(collectionName);

            command.CommandText = $"""
                                   CREATE TABLE IF NOT EXISTS {tableName}(
                                      {ImageIdColumn} BYTEA PRIMARY KEY, 
                                      {ImageHashColumn} HALFVEC(256),
                                      {ImageSimilaritiesColumn} JSONB
                                   ) WITHOUT OIDS
                                   """;

            await command.ExecuteNonQueryAsync(cancellationToken);

            command.CommandText = $"""
                                   CREATE INDEX IF NOT EXISTS {ImageIdColumn}
                                   ON {tableName}({ImageIdColumn}) INCLUDE ({ImageHashColumn}) NULLS DISTINCT
                                   """;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask<ReadOnlyMemory<Half>> GetImageHash(string collectionName, byte[] id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            var tableName = ConvertNameToSnakeCaseLower(collectionName);

            await using var command = connection.CreateCommand();

            command.CommandText =
                $"SELECT {ImageHashColumn} FROM {tableName} WHERE {ImageIdColumn} = @{ImageIdColumn}";

            command.Parameters.AddWithValue(ImageIdColumn, id);

            await using var result = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

            if (!result.HasRows)
                return Array.Empty<Half>();

            await result.ReadAsync(cancellationToken);
            var tempHash =
                await result.GetFieldValueAsync<HalfVector>(ImageHashColumn, cancellationToken: cancellationToken);

            return tempHash.Memory;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask<bool> InsertImageInfos(string collectionName, ImagesGroup group,
        CancellationToken cancellationToken = default)
    {
        // Add each fingerprint in each buckets
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            var tableName = ConvertNameToSnakeCaseLower(collectionName);

            await using var command = connection.CreateCommand();

            command.CommandText =
                $"INSERT INTO {tableName} VALUES (@{ImageIdColumn},@{ImageHashColumn},@{ImageSimilaritiesColumn})";

            command.Parameters.AddWithValue(ImageIdColumn, NpgsqlDbType.Bytea, group.Id);
            command.Parameters.AddWithValue(ImageHashColumn, new HalfVector(group.ImageHash!.Value));
            command.Parameters.AddWithValue(ImageSimilaritiesColumn, NpgsqlDbType.Jsonb, "[]");
            await command.ExecuteNonQueryAsync(cancellationToken);

            return true;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask<ConcurrentObservableDictionary<byte[], Similarity>> GetExistingSimilaritiesForImage(
        string collectionName, byte[] currentGroupId,
        CancellationToken cancellationToken = default)
    {
        var matchingFiles = new ConcurrentObservableDictionary<byte[], Similarity>(false, HashComparer);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
            var tableNameSpan =
                MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference<char>(tableName), collectionName.Length);
            tableNameSpan.Replace('-', '_');

            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                 SELECT {ImageSimilaritiesColumn}
                 FROM {tableName}
                 WHERE {ImageIdColumn} = @{ImageIdColumn}
                 """;

            command.Parameters.AddWithValue(ImageIdColumn, currentGroupId);
            await using var reader =
                await command.ExecuteReaderAsync(CommandBehavior.SingleRow | CommandBehavior.SequentialAccess,
                    cancellationToken);

            if (!reader.HasRows)
            {
                return matchingFiles;
            }

            await reader.ReadAsync(cancellationToken);

            var test = new Utf8JsonReader(
                await reader.GetFieldValueAsync<byte[]>(ImageSimilaritiesColumn, cancellationToken));

            matchingFiles.AddRange(
                JsonSerializer.Deserialize<List<Similarity>>(ref test)!.ToDictionary(
                    similarity => similarity.DuplicateId,
                    similarity => similarity));

            return matchingFiles;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask<IEnumerable<KeyValuePair<byte[], Similarity>>> GetSimilarImages(string collectionName,
        byte[] id, ReadOnlyMemory<Half> imageHash,
        decimal degreeOfSimilarity, ICollection<byte[]> groupsAlreadyDone,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();
            var tableName = ConvertNameToSnakeCaseLower(collectionName);
            command.CommandText = $"""
                                   WITH nearest_results AS MATERIALIZED (
                                       SELECT {ImageIdColumn}, {ImageHashColumn} <+> @{ImageHashColumn} AS {SimilarityScoreColumn}
                                       FROM {tableName}
                                       ORDER BY {ImageHashColumn} <+> @{ImageHashColumn}
                                       LIMIT 2000
                                   ) 
                                   SELECT * FROM nearest_results WHERE {SimilarityScoreColumn} <= @{SimilarityScoreColumn} ORDER BY {SimilarityScoreColumn}
                                   """;

            command.Parameters.AddWithValue(ImageHashColumn, new HalfVector(imageHash));

            command.Parameters.AddWithValue(SimilarityScoreColumn, NpgsqlDbType.Double,
                Convert.ToDouble(degreeOfSimilarity));

            var result = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);

            if (!result.HasRows)
            {
                return [];
            }

            var matchingIds = new List<KeyValuePair<byte[], Similarity>>();
            while (await result.ReadAsync(cancellationToken))
            {
                var matchingId = await result.GetFieldValueAsync<byte[]>(ImageIdColumn, cancellationToken);

                if (groupsAlreadyDone.Contains(matchingId, HashComparer))
                    continue;

                var score = result.GetDouble(SimilarityScoreColumn);

                matchingIds.Add(new KeyValuePair<byte[], Similarity>(matchingId,
                    new Similarity
                    {
                        OriginalId = id, DuplicateId = matchingId, Score = Convert.ToDecimal(score)
                    }));
            }

            return matchingIds;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask<bool> LinkToSimilarImagesAsync(string collectionName, byte[] id,
        ICollection<Similarity> newSimilarities,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();

            var tableName = ConvertNameToSnakeCaseLower(collectionName);

            command.CommandText =
                $"UPDATE {tableName} SET {ImageSimilaritiesColumn} = @{ImageSimilaritiesColumn} WHERE {ImageIdColumn} = @{ImageIdColumn}";

            command.Parameters.AddWithValue(ImageSimilaritiesColumn, NpgsqlDbType.Jsonb, newSimilarities);
            command.Parameters.AddWithValue(ImageIdColumn, NpgsqlDbType.Bytea, id);

            await command.ExecuteNonQueryAsync(cancellationToken);

            return true;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask DisableIndexingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();

            command.CommandText = $"DROP INDEX IF EXISTS {ImageHashColumn}_ix";

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask EnableIndexingAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = connection.CreateCommand();

            var tableName = ConvertNameToSnakeCaseLower(collectionName);

            command.CommandText =
                $"CREATE INDEX IF NOT EXISTS {ImageHashColumn}_ix ON {tableName} USING HNSW({ImageHashColumn} halfvec_l1_ops) WITH (m = 16, ef_construction = 256)";
            await command.ExecuteNonQueryAsync(cancellationToken);

            command.CommandText = "SET hnsw.iterative_scan = relaxed_order";
            await command.ExecuteNonQueryAsync(cancellationToken);

            command.CommandText = "SET hnsw.ef_search = 1000";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public async ValueTask<bool> IsIndexingDoneAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();

            command.CommandText =
                """SELECT phase, ROUND(100.0 * blocks_done / NULLIF(blocks_total, 0), 1) AS "%" FROM pg_stat_progress_create_index""";

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);

            if (!reader.HasRows)
            {
                return true;
            }

            return reader.GetString("phase").Contains("loading tuples") &&
                   Math.Abs(reader.GetDouble("%") - 100.0) <= double.Epsilon;
        }
        finally
        {
            await connection.CloseAsync();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _dataSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await _dataSource.DisposeAsync();
    }
}