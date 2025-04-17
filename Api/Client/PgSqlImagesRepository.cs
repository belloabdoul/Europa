using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Api.Client.Repositories;
using Core.Entities.Images;
using Core.Entities.Commons;
using Npgsql;
using NpgsqlTypes;
using ToolBX.Collections.ObservableDictionary;

namespace Api.Client;

public sealed class PgSqlImagesRepository : ICollectionRepository, IImageInfosRepository, IIndexingRepository,
    ISimilarImagesRepository, IDisposable, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    private bool _isDisposed;
    
    #region Images table

    private const string ImagesTableName = "images";

    private static readonly string ImageIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.Id));

    private static readonly string ImageFileHashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.FileHash));

    private static readonly string ImageHashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.Hash));

    #endregion

    #region Images similarities table

    private const string SimilaritiesTableName = "images_matches";

    private static readonly string SimilarityOriginalIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.OriginalId));

    private static readonly string SimilarityDuplicateIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.DuplicateId));

    private static readonly string SimilarityScoreColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.Score));

    private static readonly string ExistingMatchesQuery = $"""
                                                           SELECT {SimilarityDuplicateIdColumn} AS {ImageIdColumn}, {SimilarityScoreColumn}
                                                           FROM {SimilaritiesTableName}
                                                           WHERE {SimilarityOriginalIdColumn} = @{ImageIdColumn} 
                                                           UNION
                                                           SELECT {SimilarityOriginalIdColumn} AS {ImageIdColumn}, {SimilarityScoreColumn}
                                                           FROM {SimilaritiesTableName}
                                                           WHERE {SimilarityDuplicateIdColumn} = @{ImageIdColumn}
                                                           """;

    private static readonly string SimilaritySearchQueryCommand = $"""
                                                                   WITH matches AS (
                                                                       SELECT {ImageIdColumn}, {ImageHashColumn} <~> @{ImageHashColumn} AS {SimilarityScoreColumn}
                                                                       FROM {ImagesTableName}
                                                                       ORDER BY {ImageHashColumn} <~> @{ImageHashColumn}
                                                                       LIMIT 100
                                                                   ),
                                                                   ids AS (SELECT UNNEST(@existingMatches) AS id)
                                                                   SELECT matches.{ImageIdColumn}, {SimilarityScoreColumn}
                                                                   FROM matches LEFT JOIN ids ON matches.id = ids.id 
                                                                   WHERE {SimilarityScoreColumn} <= @{SimilarityScoreColumn} 
                                                                   AND ids.id IS NULL
                                                                   ORDER BY {SimilarityScoreColumn}
                                                                   """;

     private static readonly string MatchesInsertQuery = $"""
                                                          INSERT INTO {SimilaritiesTableName} 
                                                          VALUES (@{SimilarityOriginalIdColumn}, @{SimilarityDuplicateIdColumn}, @{SimilarityScoreColumn})
                                                          ON CONFLICT DO NOTHING
                                                          """;

    #endregion

    [Experimental("NPG9001")]
    public PgSqlImagesRepository(IConnectionStringBuilder connectionStringBuilder)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString);
        dataSourceBuilder.UseVector().AddTypeInfoResolverFactory(BitArrayBitStringTypeInfoResolverFactory.Instance);
        _dataSource = dataSourceBuilder.Build();
    }

    public async Task CreateTablesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        // Create a table of fingerprints and its index
        await using var command = connection.CreateCommand();

        command.CommandText = "CREATE EXTENSION IF NOT EXISTS vector";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await connection.ReloadTypesAsync(cancellationToken);

        // Images table
        command.CommandText = $"""
                               CREATE TABLE IF NOT EXISTS {ImagesTableName}(
                                  {ImageIdColumn} BIGSERIAL PRIMARY KEY, 
                                  {ImageFileHashColumn} BYTEA,
                                  {ImageHashColumn} BIT(224)
                               ) WITHOUT OIDS
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {ImageFileHashColumn}_ix
                               ON {ImagesTableName}({ImageFileHashColumn}) INCLUDE ({ImageHashColumn}) NULLS DISTINCT
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        // Images similarities tables
        command.CommandText = $"""
                               CREATE TABLE IF NOT EXISTS {SimilaritiesTableName}(
                                  {SimilarityOriginalIdColumn} BIGINT REFERENCES {ImagesTableName}({ImageIdColumn}), 
                                  {SimilarityDuplicateIdColumn} BIGINT REFERENCES {ImagesTableName}({ImageIdColumn}),
                                  {SimilarityScoreColumn} NUMERIC,
                                  PRIMARY KEY ({SimilarityOriginalIdColumn}, {SimilarityDuplicateIdColumn})
                               ) WITHOUT OIDS
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<ImageInfos> GetImageInfos(byte[] id, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText =
            $"SELECT {ImageIdColumn}, {ImageHashColumn} FROM {ImagesTableName} WHERE {ImageFileHashColumn} = @{ImageFileHashColumn}";

        var idColumn = new NpgsqlParameter<byte[]>(ImageFileHashColumn, NpgsqlDbType.Bytea);
        command.Parameters.Add(idColumn);
        await command.PrepareAsync(cancellationToken);

        idColumn.TypedValue = id;
        await using var result = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!result.HasRows)
            return new ImageInfos(0, new BitArray(0, []));

        await result.ReadAsync(cancellationToken);
        var uid = await result.GetFieldValueAsync<long>(ImageIdColumn, cancellationToken: cancellationToken);
        var hash = await result.GetFieldValueAsync<BitArray>(ImageHashColumn, cancellationToken: cancellationToken);
        return new ImageInfos(uid, hash);
    }

    public async ValueTask<long> InsertImageInfos(ImagesGroup group, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {ImagesTableName}({ImageFileHashColumn}, {ImageHashColumn}) VALUES (@{ImageFileHashColumn}, @{ImageHashColumn}) RETURNING {ImageIdColumn}";
        var idColumn = new NpgsqlParameter<byte[]>(ImageFileHashColumn, NpgsqlDbType.Bytea);
        var imageHashColumn = new NpgsqlParameter<BitArray>(ImageHashColumn, NpgsqlDbType.Bit);
        command.Parameters.Add(idColumn);
        command.Parameters.Add(imageHashColumn);
        await command.PrepareAsync(cancellationToken);

        idColumn.TypedValue = group.FileHash;
        imageHashColumn.TypedValue = group.Hash;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async ValueTask<ObservableDictionary<long, Similarity>> GetExistingSimilaritiesForImage(
        long currentGroupId, CancellationToken cancellationToken = default)
    {
        var matchingFiles = new ObservableDictionary<long, Similarity>();
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = ExistingMatchesQuery;

        var originalIdColumn = new NpgsqlParameter<long>(ImageIdColumn, NpgsqlDbType.Bigint);
        var scoreColumn = new NpgsqlParameter<decimal>(SimilarityScoreColumn, NpgsqlDbType.Numeric);
        command.Parameters.Add(originalIdColumn);
        command.Parameters.Add(scoreColumn);
        await command.PrepareAsync(cancellationToken);

        originalIdColumn.TypedValue = currentGroupId;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!reader.HasRows)
        {
            return matchingFiles;
        }

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = await reader.GetFieldValueAsync<long>(ImageIdColumn, cancellationToken: cancellationToken);
            matchingFiles.Add(id,
                new Similarity
                {
                    OriginalId = currentGroupId, DuplicateId = id,
                    Score = await reader.GetFieldValueAsync<decimal>(SimilarityScoreColumn, cancellationToken)
                });
        }

        return matchingFiles;
    }

    public async IAsyncEnumerable<KeyValuePair<long, Similarity>> GetSimilarImages(long id, BitArray imageHash,
        decimal degreeOfSimilarity, IList<long> existingMatches,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = SimilaritySearchQueryCommand;
        var imageHashColumn = new NpgsqlParameter<BitArray>(ImageHashColumn, NpgsqlDbType.Bit);
        var existingMatchesColumn =
            new NpgsqlParameter<IList<long>>("existingMatches", NpgsqlDbType.Array | NpgsqlDbType.Bigint);
        var similarityScoreColumn = new NpgsqlParameter<decimal>(SimilarityScoreColumn, NpgsqlDbType.Numeric);

        command.Parameters.Add(imageHashColumn);
        command.Parameters.Add(existingMatchesColumn);
        command.Parameters.Add(similarityScoreColumn);
        await command.PrepareAsync(cancellationToken);

        imageHashColumn.TypedValue = imageHash;
        existingMatchesColumn.TypedValue = existingMatches;
        similarityScoreColumn.TypedValue = degreeOfSimilarity;

        var result = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);

        if (!result.HasRows)
        {
            yield break;
        }

        while (await result.ReadAsync(cancellationToken))
        {
            var matchingId = await result.GetFieldValueAsync<long>(ImageIdColumn, cancellationToken);

            var score = await result.GetFieldValueAsync<double>(SimilarityScoreColumn, cancellationToken);

            yield return new KeyValuePair<long, Similarity>(matchingId,
                new Similarity
                {
                    OriginalId = id, DuplicateId = matchingId, Score = Convert.ToDecimal(score)
                });
        }
    }

    public async ValueTask<bool> LinkToSimilarImagesAsync(long id, ICollection<Similarity> newSimilarities,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = MatchesInsertQuery;

        var originalIdColumn = new NpgsqlParameter<long>(SimilarityOriginalIdColumn, NpgsqlDbType.Bigint);
        var duplicateIdColumn = new NpgsqlParameter<long>(SimilarityDuplicateIdColumn, NpgsqlDbType.Bigint);
        var scoreColumn = new NpgsqlParameter<decimal>(SimilarityScoreColumn, NpgsqlDbType.Numeric);

        command.Parameters.Add(originalIdColumn);
        command.Parameters.Add(duplicateIdColumn);
        command.Parameters.Add(scoreColumn);

        await command.PrepareAsync(cancellationToken);

        foreach (var similarity in newSimilarities)
        {
            originalIdColumn.TypedValue = similarity.OriginalId;
            duplicateIdColumn.TypedValue = similarity.DuplicateId;
            scoreColumn.TypedValue = similarity.Score;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return true;
    }

    public async ValueTask DisableIndexingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText = $"DROP INDEX IF EXISTS {ImageHashColumn}_ix";

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask EnableIndexingAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        command.CommandText =
            $"CREATE INDEX IF NOT EXISTS {ImageHashColumn}_ix ON {ImagesTableName} USING HNSW({ImageHashColumn} bit_hamming_ops) WITH (m = 16, ef_construction = 256)";
        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = "SET hnsw.iterative_scan = relaxed_order";
        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = "SET hnsw.ef_search = 1000";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<bool> IsIndexingDoneAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);


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

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _dataSource.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        await _dataSource.DisposeAsync();
    }
}