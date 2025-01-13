using System.Collections.Concurrent;
using System.Data;
using System.Data.SQLite;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Api.Client.Repositories;
using Core.Entities.Audios;
using Core.Entities.Commons;
using DotNext.Text;
using Swordfish.NET.Collections;
using Fingerprint = Core.Entities.Audios.Fingerprint;

namespace Api.Client;

public sealed class SqLiteAudioRepository : ICollectionRepository, IAudioInfosRepository, IIndexingRepository,
    ISimilarAudiosRepository
{
    private static readonly string LocalFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string Audios = Path.GetRelativePath(Environment.CurrentDirectory,
        string.Join(Path.DirectorySeparatorChar, LocalFolder, "Europa", "audios"));

    private static readonly HashComparer HashComparer = new();
    
    private bool _isDisposed;

    private readonly string _connectionString;

    private readonly SQLiteConnection _connection;

    private static readonly string FileHashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.FileHash));

    private static readonly string FingerprintsCountColumn = nameof(ConcurrentQueue<Fingerprint>.Count);

    private static readonly string StartAtColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.StartAt));

    private static readonly string HashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.HashBins));

    private static readonly string MatchesTable =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(AudiosGroup.Matches));

    private static readonly string OriginalIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.OriginalId));

    private static readonly string DuplicateIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.DuplicateId));

    private static readonly string ScoreColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.Score));

    private static readonly string MatchingFingerprintsQuery = $"""
                                                                SELECT {FileHashColumn}, {StartAtColumn}
                                                                FROM hashes
                                                                WHERE {HashColumn} IN (
                                                                    @{HashColumn}_0,
                                                                    @{HashColumn}_1,
                                                                    @{HashColumn}_2,
                                                                    @{HashColumn}_3,
                                                                    @{HashColumn}_4,
                                                                    @{HashColumn}_5,
                                                                    @{HashColumn}_6,
                                                                    @{HashColumn}_7,
                                                                    @{HashColumn}_8,
                                                                    @{HashColumn}_9,
                                                                    @{HashColumn}_10,
                                                                    @{HashColumn}_11,
                                                                    @{HashColumn}_12,
                                                                    @{HashColumn}_13,
                                                                    @{HashColumn}_14,
                                                                    @{HashColumn}_15,
                                                                    @{HashColumn}_16,
                                                                    @{HashColumn}_17,
                                                                    @{HashColumn}_18,
                                                                    @{HashColumn}_19,
                                                                    @{HashColumn}_20,
                                                                    @{HashColumn}_21,
                                                                    @{HashColumn}_22,
                                                                    @{HashColumn}_23,
                                                                    @{HashColumn}_24
                                                                ) AND {StartAtColumn} BETWEEN @start AND @end
                                                                GROUP BY {FileHashColumn}, {StartAtColumn}
                                                                HAVING COUNT(*) >= @thresholdVotes
                                                                """;
    //
    // 

    public SqLiteAudioRepository()
    {
        Directory.CreateDirectory(Directory.GetParent(Audios)!.FullName);
        var connectionStringBuilder = new SQLiteConnectionStringBuilder
        {
            DataSource = $"{Audios}.sqlite", BusyTimeout = int.MaxValue, CacheSize = 10000
        };
        connectionStringBuilder.Add("cache", "shared");
        _connectionString = connectionStringBuilder.ToString();
        _connection = new SQLiteConnection(_connectionString);
        _connection.Open();
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
        var tableName = ConvertNameToSnakeCaseLower(collectionName);

        // Create a table of file and its fingerprints count
        await using var command = _connection.CreateCommand();
        command.CommandText = $"""
                                CREATE TABLE IF NOT EXISTS {tableName}(
                                   {FileHashColumn} BLOB PRIMARY KEY,
                                   {FingerprintsCountColumn} INTEGER NOT NULL
                                )
                               """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {FileHashColumn}_ix
                               ON {tableName}({FileHashColumn})
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        // Create the table of hashes and its index
        command.CommandText = $"""
                               CREATE TABLE IF NOT EXISTS hashes(
                                  {FileHashColumn} BLOB,
                                  {StartAtColumn} REAL,
                                  {HashColumn} BLOB)
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        // Create the table of file matches and its index
        command.CommandText = $"""
                                CREATE TABLE IF NOT EXISTS {MatchesTable}(
                                   {OriginalIdColumn} BLOB, 
                                   {DuplicateIdColumn} BLOB, 
                                   {ScoreColumn} REAL
                                )
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {OriginalIdColumn}
                               ON {MatchesTable}({OriginalIdColumn})
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<int> GetFingerprintsCount(string collectionName, byte[] id,
        CancellationToken cancellationToken = default)
    {
        var tableName = ConvertNameToSnakeCaseLower(collectionName);

        await using var command = _connection.CreateCommand();

        command.CommandText =
            $"SELECT {FingerprintsCountColumn} FROM {tableName} WHERE {FileHashColumn} = @{FileHashColumn}";
        command.Parameters.AddWithValue(FileHashColumn, id);

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async ValueTask<int> InsertFingerprintsAsync(string collectionName, IList<Fingerprint> fingerprints,
        CancellationToken cancellationToken = default)
    {
        var tableName = ConvertNameToSnakeCaseLower(collectionName);
        var id = fingerprints.First().FileHash;

        await using var transaction = _connection.BeginTransaction();
        // Associate file with fingerprint count
        await using var command = _connection.CreateCommand();
        command.CommandText =
            $"INSERT INTO hashes VALUES(@{FileHashColumn}, @{StartAtColumn}, @{HashColumn})";
        command.Parameters.Add(new SQLiteParameter(FileHashColumn, DbType.Binary));
        command.Parameters.Add(new SQLiteParameter(StartAtColumn, DbType.Double));
        command.Parameters.Add(new SQLiteParameter(HashColumn, DbType.Binary));
        // Batch insert the hashes
        var hash = GC.AllocateUninitializedArray<byte>(sizeof(int) + sizeof(byte));
        foreach (var fingerprint in fingerprints)
        {
            for (byte j = 0; j < Fingerprint.BucketCount; j++)
            {
                command.Parameters[FileHashColumn].Value = fingerprint.FileHash;
                command.Parameters[StartAtColumn].Value = fingerprint.StartAt;
                hash[0] = j;
                BitConverter.TryWriteBytes(hash.AsSpan()[1..], fingerprint.HashBins[j]);
                command.Parameters[HashColumn].Value = hash;

                await command.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        command.CommandText = $"INSERT INTO {tableName} VALUES (@{FileHashColumn}, @{FingerprintsCountColumn})";
        command.Parameters.AddWithValue(FileHashColumn, id);
        command.Parameters.AddWithValue(FingerprintsCountColumn, fingerprints.Count);

        await command.ExecuteNonQueryAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return fingerprints.Count;
    }

    public async ValueTask<ConcurrentObservableDictionary<byte[], Similarity>> GetExistingMatchesForFileAsync(
        string collectionName, byte[] fileId, CancellationToken cancellationToken = default)
    {
        var matchingFiles = new ConcurrentObservableDictionary<byte[], Similarity>(true, HashComparer);

        await using var command = _connection.CreateCommand();
        command.CommandText = $"""
                               SELECT {DuplicateIdColumn}, {ScoreColumn}
                               FROM {MatchesTable}
                               WHERE {OriginalIdColumn} = @{OriginalIdColumn}
                               """;

        command.Parameters.AddWithValue(OriginalIdColumn, fileId);
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
        var matches = new ConcurrentDictionary<byte[], ConcurrentObservableSortedSet<double>>(HashComparer);

        await Parallel.ForEachAsync(fingerprints, cancellationToken,
            async (fingerprint, searchToken) =>
            {
                await using var command = _connection.CreateCommand();

                command.CommandText = MatchingFingerprintsQuery;

                for (var j = 0; j < Fingerprint.BucketCount; j++)
                {
                    command.Parameters.Add($"{HashColumn}_{j}", DbType.Binary);
                }

                command.Parameters.Add("start", DbType.Double);
                command.Parameters.Add("end", DbType.Double);

                command.Parameters.AddWithValue("thresholdVotes", thresholdVotes);

                // foreach (var fingerprint in fingerprints)
                // {
                for (var j = 0; j < Fingerprint.BucketCount; j++)
                {
                    command.Parameters[$"{HashColumn}_{j}"].Value = $"{fingerprint.HashBins[j]}:{j}".AsMemory();
                }

                command.Parameters["start"].Value = fingerprint.StartAt - gapAllowed;
                command.Parameters["end"].Value = fingerprint.StartAt + gapAllowed;

                await using var reader = await command.ExecuteReaderAsync(searchToken);

                if (!reader.HasRows)
                    return;

                while (await reader.ReadAsync(searchToken))
                {
                    var id = await reader.GetFieldValueAsync<byte[]>(FileHashColumn, searchToken);

                    // if (groupsAlreadyDone.Contains(id))
                    //     continue;

                    var matchingFingerprints = matches.GetOrAdd(id, []);
                    matchingFingerprints.Add(await reader.GetFieldValueAsync<double>(StartAtColumn,
                        searchToken));
                }
            });

        return matches
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
        // await using var connection = new SQLiteConnection(ConnectionString);
        // await connection.OpenAsync(cancellationToken);
        //
        // await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        //
        // foreach (var batch in newSimilarities.Chunk(1000))
        // {
        //     using var appender = connection.CreateAppender(MatchesTable);
        //     foreach (var similarity in batch)
        //     {
        //         appender
        //             .CreateRow()
        //             .AppendValue(similarity.OriginalId)
        //             .AppendValue(similarity.DuplicateId)
        //             .AppendValue(similarity.Score);
        //     }
        // }
        //
        // await transaction.CommitAsync(cancellationToken);
        // return true;
        return true;
    }

    public async ValueTask DisableIndexingAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();

        command.CommandText = $"DROP INDEX IF EXISTS {HashColumn}_ix";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask EnableIndexingAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {HashColumn}_ix
                               ON hashes({HashColumn}, {StartAtColumn})
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<bool> IsIndexingDoneAsync(string collectionName,
        CancellationToken cancellationToken = default)
    {
        // throw new NotImplementedException();
        return true;
    }
}