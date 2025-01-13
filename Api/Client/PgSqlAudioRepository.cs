using System.Collections.Concurrent;
using System.Data;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using Api.Client.Repositories;
using Core.Entities.Audios;
using Core.Entities.Commons;
using DotNext.Collections.Generic;
using Npgsql;
using NpgsqlTypes;
using Swordfish.NET.Collections;
using Fingerprint = Core.Entities.Audios.Fingerprint;

namespace Api.Client;

public sealed class PgSqlAudioRepository : ICollectionRepository, IAudioInfosRepository, IIndexingRepository,
    ISimilarAudiosRepository
{
    private readonly NpgsqlDataSource _dataSource;

    private bool _isDisposed;

    private static readonly HashComparer HashComparer = new();

    private static readonly string FileHashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.FileHash));

    private static readonly string StartAtColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.StartAt));

    private static readonly string HashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.HashBins));

    private static readonly string FingerprintsCountColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ConcurrentQueue<Fingerprint>.Count));

    private static readonly string MatchesTable =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(AudiosGroup.Matches));

    private static readonly string OriginalIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.OriginalId));

    private static readonly string DuplicateIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.DuplicateId));

    private static readonly string ScoreColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.Score));

    private static readonly string MatchingFingerprintsQuery = $"""
                                                                WITH matches AS (
                                                                SELECT {FileHashColumn}, {StartAtColumn}
                                                                FROM hashes
                                                                WHERE {HashColumn} IN (
                                                                    @0,
                                                                    @1,
                                                                    @2,
                                                                    @3,
                                                                    @4,
                                                                    @5,
                                                                    @6,
                                                                    @7,
                                                                    @8,
                                                                    @9,
                                                                    @10,
                                                                    @11,
                                                                    @12,
                                                                    @13,
                                                                    @14,
                                                                    @15,
                                                                    @16,
                                                                    @17,
                                                                    @18,
                                                                    @19,
                                                                    @20,
                                                                    @21,
                                                                    @22,
                                                                    @23,
                                                                    @24
                                                                ) AND {StartAtColumn} BETWEEN @start AND @end
                                                                GROUP BY {FileHashColumn}, {StartAtColumn}
                                                                HAVING COUNT(*) >= @thresholdVotes),
                                                                ids AS (SELECT UNNEST(@existingMatches) AS id)
                                                                SELECT {FileHashColumn}, {StartAtColumn}
                                                                FROM matches
                                                                WHERE NOT EXISTS (SELECT 1
                                                                FROM ids
                                                                WHERE id = {FileHashColumn})
                                                                """;

    public PgSqlAudioRepository(IConnectionStringBuilder connectionStringBuilder)
    {
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionStringBuilder.ConnectionString);
        dataSourceBuilder.EnableDynamicJson();
        _dataSource = dataSourceBuilder.Build();
    }

    private static string ConvertNameToSnakeCaseLower(string name)
    {
        var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
        var tableNameSpan =
            MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference<char>(tableName), name.Length);
        tableNameSpan.Replace('-', '_');
        return tableName;
    }

    public async ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();

        // Create a table of fingerprints and its indexes: one on the file hash and the other on the second at which the
        // fingerprint starts
        var tableName = ConvertNameToSnakeCaseLower(collectionName);
        command.CommandText = $"""
                                CREATE TABLE IF NOT EXISTS {tableName}(
                                   {FileHashColumn} BYTEA, 
                                   {FingerprintsCountColumn} INTEGER
                                ) WITHOUT OIDS
                               """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        // File hash
        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {FileHashColumn}_ix
                               ON {tableName}({FileHashColumn}) NULLS DISTINCT
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        // The fingerprint table
        command.CommandText = $"""
                               CREATE TABLE IF NOT EXISTS hashes(
                                  {FileHashColumn} BYTEA,
                                  {StartAtColumn} DOUBLE PRECISION,
                                  {HashColumn} BYTEA
                               ) WITHOUT OIDS
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        // Create the table of file matches and its index
        command.CommandText = $"""
                                CREATE TABLE IF NOT EXISTS {MatchesTable}(
                                   {OriginalIdColumn} BYTEA, 
                                   {DuplicateIdColumn} BYTEA, 
                                   {ScoreColumn} DOUBLE PRECISION
                                ) WITHOUT OIDS
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {OriginalIdColumn}
                               ON {MatchesTable}({OriginalIdColumn}) NULLS DISTINCT
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisableIndexingAsync(string collectionName,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();


        command.CommandText = $"DROP INDEX IF EXISTS {HashColumn}_ix";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask EnableIndexingAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {HashColumn}_ix
                               ON hashes({HashColumn}, {StartAtColumn}) NULLS DISTINCT
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<bool> IsIndexingDoneAsync(string collectionName,
        CancellationToken cancellationToken = default)
    {
        return true;
    }

    public async ValueTask<int> GetFingerprintsCount(string collectionName, byte[] id,
        CancellationToken cancellationToken = default)
    {
        var tableName = ConvertNameToSnakeCaseLower(collectionName);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            $"SELECT COALESCE({FingerprintsCountColumn}, 0) FROM {tableName} WHERE {FileHashColumn} = @{FileHashColumn}";
        command.Parameters.Add(FileHashColumn, NpgsqlDbType.Bytea);
        await command.PrepareAsync(cancellationToken);

        command.Parameters[FileHashColumn].Value = id;
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async ValueTask<int> InsertFingerprintsAsync(string collectionName, IList<Fingerprint> fingerprints,
        CancellationToken cancellationToken = default)
    {
        var tableName = ConvertNameToSnakeCaseLower(collectionName);

        var bulkInsertHashes =
            $"COPY hashes({FileHashColumn}, {StartAtColumn}, {HashColumn}) FROM STDIN (FORMAT BINARY)";

        // Start a transaction because we want everything done at once
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var id = fingerprints[0].FileHash;

        try
        {
            await using var binaryImporter =
                await connection.BeginBinaryImportAsync(bulkInsertHashes, cancellationToken);
            var hash = GC.AllocateUninitializedArray<byte>(sizeof(byte) + sizeof(int));
            foreach (var fingerprint in fingerprints)
            {
                for (byte j = 0; j < Fingerprint.BucketCount; j++)
                {
                    await binaryImporter.StartRowAsync(cancellationToken);
                    await binaryImporter.WriteAsync(id, NpgsqlDbType.Bytea, cancellationToken);
                    await binaryImporter.WriteAsync(fingerprint.StartAt, cancellationToken);
                    hash[0] = j;
                    Unsafe.WriteUnaligned(ref MemoryMarshal.GetReference(hash.AsSpan(1)), fingerprint.HashBins[j]);
                    await binaryImporter.WriteAsync(hash, NpgsqlDbType.Bytea, cancellationToken);
                }
            }

            await binaryImporter.CompleteAsync(cancellationToken);
            await binaryImporter.CloseAsync(cancellationToken);

            // Commit after everything is inserted so that if a cancellation is requested, nothing is saved
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"INSERT INTO {tableName}({FileHashColumn}, {FingerprintsCountColumn}) VALUES (@{FileHashColumn}, @{FingerprintsCountColumn})";
            command.Parameters.Add(FileHashColumn, NpgsqlDbType.Bytea);
            command.Parameters.Add(FingerprintsCountColumn, NpgsqlDbType.Integer);
            await command.PrepareAsync(cancellationToken);

            command.Parameters[FileHashColumn].Value = id;
            command.Parameters[FingerprintsCountColumn].Value = fingerprints.Count;

            await command.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return fingerprints.Count;
        }
        catch (PostgresException)
        {
            // TODO log here and rollback
            await transaction.RollbackAsync(cancellationToken);
            return 0;
        }
    }

    public async ValueTask<ConcurrentObservableDictionary<byte[], Similarity>> GetExistingMatchesForFileAsync(
        string collectionName, byte[] fileId, CancellationToken cancellationToken = default)
    {
        var matchingFiles = new ConcurrentObservableDictionary<byte[], Similarity>(true, HashComparer);

        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
                               SELECT {DuplicateIdColumn}, {ScoreColumn}
                               FROM {MatchesTable}
                               WHERE {OriginalIdColumn} = @{OriginalIdColumn}
                               """;
        command.Parameters.Add(OriginalIdColumn, NpgsqlDbType.Bytea);
        await command.PrepareAsync(cancellationToken);
        command.Parameters[OriginalIdColumn].Value = fileId;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!reader.HasRows)
            return matchingFiles;

        while (await reader.ReadAsync(cancellationToken))
        {
            var duplicateId =
                await reader.GetFieldValueAsync<byte[]>(DuplicateIdColumn, cancellationToken);

            matchingFiles.Add(duplicateId,
                new Similarity
                {
                    OriginalId = fileId, DuplicateId = duplicateId,
                    Score = Convert.ToDecimal(
                        await reader.GetFieldValueAsync<double>(ScoreColumn, cancellationToken))
                });
        }

        return matchingFiles;
    }

    public async ValueTask<IEnumerable<KeyValuePair<byte[], decimal>>> GetMatchingFingerprintsAsync(
        string collectionName, IList<Fingerprint> fingerprints, int thresholdVotes, double gapAllowed,
        decimal degreeOfSimilarity, byte[] fileId, ICollection<byte[]> existingMatches,
        Dictionary<byte[], int> filesWithFingerprintsCount, CancellationToken cancellationToken = default)
    {
        var matchingFingerprints = new Dictionary<byte[], HashSet<double>>(HashComparer);
        await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = MatchingFingerprintsQuery;

        for (var i = 0; i < Fingerprint.BucketCount; i++)
        {
            command.Parameters.Add(i.ToString(), NpgsqlDbType.Bytea);
        }

        command.Parameters.Add("start", NpgsqlDbType.Double);
        command.Parameters.Add("end", NpgsqlDbType.Double);
        command.Parameters.Add("thresholdVotes", NpgsqlDbType.Integer);
        command.Parameters.Add("existingMatches", NpgsqlDbType.Array | NpgsqlDbType.Bytea);
        await command.PrepareAsync(cancellationToken);

        var hashes = new byte[25][];
        for (var i = 0; i < Fingerprint.BucketCount; i++)
        {
            hashes[i] = GC.AllocateUninitializedArray<byte>(sizeof(byte) + sizeof(int));
        }

        foreach (var fingerprint in fingerprints)
        {
            for (byte i = 0; i < Fingerprint.BucketCount; i++)
            {
                hashes[i][0] = i;
                BitConverter.TryWriteBytes(hashes[i].AsSpan()[1..], fingerprint.HashBins[i]);
                command.Parameters[i.ToString()].Value = hashes[i];
            }

            command.Parameters["start"].Value = fingerprint.StartAt - gapAllowed;
            command.Parameters["end"].Value = fingerprint.StartAt + gapAllowed;
            command.Parameters["thresholdVotes"].Value = thresholdVotes;
            command.Parameters["existingMatches"].Value = existingMatches;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!reader.HasRows)
                continue;

            while (await reader.ReadAsync(cancellationToken))
            {
                var id = reader.GetFieldValue<byte[]>(FileHashColumn);
                if (!filesWithFingerprintsCount.TryGetValue(id, out _))
                    continue;

                var matches = matchingFingerprints.GetOrAdd(id, []);
                matches.Add(reader.GetFieldValue<double>(StartAtColumn));
            }
        }

        return matchingFingerprints
            .Select(match =>
            {
                var score = decimal.Min(decimal.Divide(match.Value.Count, filesWithFingerprintsCount[match.Key]),
                    decimal.Divide(match.Value.Count, filesWithFingerprintsCount[fileId]));
                if (score >= degreeOfSimilarity)
                    Console.WriteLine(
                        $"{Convert.ToHexStringLower(fingerprints[0].FileHash)} {Convert.ToHexStringLower(match.Key)} {match.Value.Count} / {filesWithFingerprintsCount[match.Key]}");
                return new KeyValuePair<byte[], decimal>(match.Key, score);
            })
            .Where(group => group.Value >= degreeOfSimilarity);
    }

    public async ValueTask<bool> LinkToSimilarFilesAsync(string collectionName, byte[] id,
        ICollection<Similarity> newSimilarities, CancellationToken cancellationToken = default)
    {
        // await using var transaction =
        //     await _writeConnection.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        //
        // try
        // {
        //     await using var command = _writeConnection.CreateCommand();
        //
        //     command.CommandText =
        //         $"INSERT INTO {MatchingFilesTable} VALUES(@{MatchingFilesOriginalIdColumn}, @{MatchingFilesDuplicateIdColumn}, @{MatchingFilesScoreColumn})";
        //
        //     foreach (var similarity in newSimilarities)
        //     {
        //         command.Parameters.AddWithValue(MatchingFilesOriginalIdColumn, similarity.OriginalId);
        //         command.Parameters.AddWithValue(MatchingFilesDuplicateIdColumn, similarity.DuplicateId);
        //         command.Parameters.AddWithValue(MatchingFilesScoreColumn, similarity.Score);
        //         await command.PrepareAsync(cancellationToken);
        //         await command.ExecuteNonQueryAsync(cancellationToken);
        //     }
        //
        //     await transaction.CommitAsync(cancellationToken);
        // }
        // catch (OperationCanceledException)
        // {
        //     await transaction.RollbackAsync(cancellationToken);
        // }

        return true;
    }
}