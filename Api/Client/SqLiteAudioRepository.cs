using System.Collections.Concurrent;
using System.Data;
using System.Data.SQLite;
using System.Runtime.InteropServices;
using System.Text.Json;
using Api.Client.Repositories;
using Core.Entities.Audios;
using Core.Entities.Commons;
using Core.Entities.Images;
using NSwag.Collections;
using Fingerprint = Core.Entities.Audios.Fingerprint;

namespace Api.Client;

public sealed class SqLiteAudioRepository : ICollectionRepository, IAudioInfosRepository,
    ISimilarAudiosRepository, IDisposable, IAsyncDisposable
{
    private static readonly string LocalFolder =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string Images = Path.GetRelativePath(Environment.CurrentDirectory,
        string.Join(Path.DirectorySeparatorChar, LocalFolder, "Europa", "audios.sqlite"));

    private static readonly HashComparer HashComparer = new();

    private readonly string _connectionString = new SQLiteConnectionStringBuilder
        {
            DataSource = Images, Pooling = true
        }
        .ConnectionString;

    private ConcurrentDictionary<int, string> _matchesQueries = new();

    private readonly SQLiteConnection _connection;

    private bool _isDisposed;

    private static readonly string FingerprintIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.Id));

    private static readonly string FingerprintFileHashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.FileHash));

    private static readonly string FingerprintStartAtColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.StartAt));

    private static readonly string FingerprintHashColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.HashBins));

    private static readonly string MatchingFilesTable =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(AudiosGroup.Matches));

    private static readonly string MatchingFilesOriginalIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.OriginalId));

    private static readonly string MatchingFilesDuplicateIdColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.DuplicateId));

    private static readonly string MatchingFilesScoreColumn =
        JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.Score));

    public SqLiteAudioRepository()
    {
        var dbLocation = Path.GetDirectoryName(Images);
        if (!Directory.Exists(dbLocation))
            Directory.CreateDirectory(dbLocation!);
        _connection = new SQLiteConnection(_connectionString);
        _connection.Open();
    }

    public async ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
    {
        // await using var _connection = new SQLiteConnection(_connectionString);
        // await _connection.OpenAsync(cancellationToken);
        await using var command = _connection.CreateCommand();

        var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
        var tableNameSpan =
            MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference<char>(tableName), collectionName.Length);
        tableNameSpan.Replace('-', '_');

        // Create a table of fingerprints and its index
        command.CommandText = $"""
                                CREATE TABLE IF NOT EXISTS {tableName}(
                                   {FingerprintIdColumn} BLOB PRIMARY KEY,
                                   {FingerprintFileHashColumn} BLOB, 
                                   {FingerprintStartAtColumn} REAL, 
                                   {FingerprintHashColumn} JSON
                                ) WITHOUT ROWID
                               """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {FingerprintFileHashColumn}
                               ON {tableName}({FingerprintFileHashColumn})
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        for (var i = 0; i < Fingerprint.BucketCount; i++)
        {
            command.CommandText = $"""
                                   CREATE INDEX IF NOT EXISTS {FingerprintHashColumn}_{i}
                                   ON {tableName}({FingerprintHashColumn} ->> {i}, {FingerprintStartAtColumn})
                                   """;

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        // Create the table of file matches and its index
        command.CommandText = $"""
                                CREATE TABLE IF NOT EXISTS {MatchingFilesTable}(
                                   {MatchingFilesOriginalIdColumn} BLOB, 
                                   {MatchingFilesDuplicateIdColumn} BLOB, 
                                   {MatchingFilesScoreColumn} REAL
                                )
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);

        command.CommandText = $"""
                               CREATE INDEX IF NOT EXISTS {MatchingFilesOriginalIdColumn}
                               ON {MatchingFilesTable}({MatchingFilesOriginalIdColumn})
                               """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask<bool> IsAlreadyInsertedAsync(string collectionName, byte[] id,
        int estimatedNumberOfFingerprints, CancellationToken cancellationToken = default)
    {
        // await using var _connection = new SQLiteConnection(_connectionString);
        // await _connection.OpenAsync(cancellationToken);
        await using var command = _connection.CreateCommand();

        var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
        var tableNameSpan =
            MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference<char>(tableName), collectionName.Length);
        tableNameSpan.Replace('-', '_');

        command.CommandText =
            $"SELECT COUNT (DISTINCT {FingerprintIdColumn}) FROM {tableName} WHERE {FingerprintFileHashColumn} = @{FingerprintFileHashColumn}";
        command.Parameters.AddWithValue(FingerprintFileHashColumn, id);

        var found = long.Abs(Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) -
                             estimatedNumberOfFingerprints) <= 5;

        return found;
    }

    public async ValueTask<bool> InsertFingerprintsAsync(string collectionName, List<Fingerprint> fingerprints,
        CancellationToken cancellationToken = default)
    {
        // Add each fingerprint in each buckets
        // await using var connection = new SQLiteConnection(_connectionString);
        // await connection.OpenAsync(cancellationToken);
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);
        try
        {
            var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
            var tableNameSpan =
                MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference<char>(tableName), collectionName.Length);
            tableNameSpan.Replace('-', '_');

            await using var command = _connection.CreateCommand();

            command.CommandText =
                $"INSERT INTO {tableName} VALUES(@{FingerprintIdColumn}, @{FingerprintFileHashColumn}, @{FingerprintStartAtColumn}, @{FingerprintHashColumn})";

            foreach (var fingerprint in fingerprints)
            {
                fingerprint.Id = Guid.CreateVersion7();
                // for (var i = 0; i < Fingerprint.BucketCount; i++)
                // {
                // command.Parameters.AddWithValue("bucket", i);
                command.Parameters.AddWithValue(FingerprintIdColumn, fingerprint.Id);
                command.Parameters.AddWithValue(FingerprintFileHashColumn, fingerprint.FileHash);
                command.Parameters.AddWithValue(FingerprintStartAtColumn, fingerprint.StartAt);
                command.Parameters.AddWithValue(FingerprintHashColumn, JsonSerializer.Serialize(fingerprint.HashBins));

                await command.ExecuteNonQueryAsync(cancellationToken);
                // }
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return true;
    }

    public async ValueTask<ObservableDictionary<byte[], Similarity>> GetExistingMatchesForFileAsync(
        string collectionName, byte[] fileId, CancellationToken cancellationToken = default)
    {
        var matchingFiles = new ObservableDictionary<byte[], Similarity>(HashComparer);

        var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
        var tableNameSpan =
            MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference<char>(tableName), collectionName.Length);
        tableNameSpan.Replace('-', '_');

        await using var command = _connection.CreateCommand();
        command.CommandText =
            $"""
             SELECT {MatchingFilesDuplicateIdColumn}, {MatchingFilesScoreColumn}
             FROM {MatchingFilesTable}
             WHERE {MatchingFilesOriginalIdColumn} = @{MatchingFilesOriginalIdColumn}
             """;

        command.Parameters.AddWithValue(MatchingFilesOriginalIdColumn, fileId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!reader.HasRows)
            return matchingFiles;

        while (await reader.ReadAsync(cancellationToken))
        {
            var duplicateId =
                await reader.GetFieldValueAsync<byte[]>(MatchingFilesDuplicateIdColumn, cancellationToken);
            matchingFiles.Add(duplicateId,
                new Similarity
                {
                    OriginalId = fileId, DuplicateId = duplicateId,
                    Score = Convert.ToDecimal(
                        await reader.GetFieldValueAsync<double>(MatchingFilesScoreColumn, cancellationToken))
                });
        }

        return matchingFiles;
    }

    public async ValueTask<IEnumerable<KeyValuePair<byte[], double>>> GetMatchingFingerprintsAsync(
        string collectionName, Fingerprint fingerprint, byte thresholdVotes, float gapAllowed,
        ICollection<byte[]> groupsAlreadyDone, CancellationToken cancellationToken = default)
    {
        var matchingFingerprints = new ConcurrentDictionary<Guid, Fingerprint>();

        var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
        var tableNameSpan =
            MemoryMarshal.CreateSpan(ref MemoryMarshal.GetReference<char>(tableName), collectionName.Length);
        tableNameSpan.Replace('-', '_');

        if (_matchesQueries.IsEmpty)
        {
            for (var i = 0; i < Fingerprint.BucketCount; i++)
            {
                _matchesQueries.TryAdd(i, $"""
                                           SELECT {FingerprintIdColumn}, {FingerprintFileHashColumn}, {FingerprintStartAtColumn}
                                           FROM {tableName}
                                           WHERE {FingerprintHashColumn} ->> {i} = @{FingerprintHashColumn}
                                           AND {FingerprintFileHashColumn} NOT IN (@{FingerprintFileHashColumn})
                                           AND {FingerprintStartAtColumn} BETWEEN @start AND @end
                                           """);
            }
        }


        await using var command = _connection.CreateCommand();

        for (var i = 0; i < Fingerprint.BucketCount; i++)
        {
            command.CommandText = _matchesQueries[i];

            command.Parameters.AddWithValue(FingerprintHashColumn, fingerprint.HashBins[i]);

            var idsDone = JsonSerializer.Serialize(groupsAlreadyDone);
            idsDone = idsDone.Substring(1, idsDone.Length - 2);
            command.Parameters.AddWithValue(FingerprintFileHashColumn, idsDone);
            command.Parameters.AddWithValue("start", fingerprint.StartAt - gapAllowed);
            command.Parameters.AddWithValue("end", fingerprint.StartAt + gapAllowed);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            if (!reader.HasRows)
                continue;

            while (await reader.ReadAsync(cancellationToken))
            {
                var match = matchingFingerprints.GetOrAdd(
                    MemoryMarshal.Read<Guid>(
                        await reader.GetFieldValueAsync<byte[]>(FingerprintIdColumn, cancellationToken)),
                    new Fingerprint());
                if (match.FileHash.Length == 0)
                {
                    match.FileHash =
                        await reader.GetFieldValueAsync<byte[]>(FingerprintFileHashColumn, cancellationToken);
                    match.StartAt =
                        await reader.GetFieldValueAsync<double>(FingerprintStartAtColumn, cancellationToken);
                }

                Interlocked.Increment(ref match.Score);
            }
        }

        return matchingFingerprints.Values.Where(matchedFingerprint => matchedFingerprint.Score >= thresholdVotes)
            .Select(matchingFingerprint =>
                new KeyValuePair<byte[], double>(matchingFingerprint.FileHash, matchingFingerprint.StartAt));
    }

    public async ValueTask<bool> LinkToSimilarFilesAsync(string collectionName, byte[] id,
        ICollection<Similarity> newSimilarities, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _connection.BeginTransactionAsync(cancellationToken);

        try
        {
            await using var command = _connection.CreateCommand();

            command.CommandText =
                $"INSERT INTO {MatchingFilesTable} VALUES(@{MatchingFilesOriginalIdColumn}, @{MatchingFilesDuplicateIdColumn}, @{MatchingFilesScoreColumn})";

            foreach (var similarity in newSimilarities)
            {
                command.Parameters.AddWithValue(MatchingFilesOriginalIdColumn, similarity.OriginalId);
                command.Parameters.AddWithValue(MatchingFilesDuplicateIdColumn, similarity.DuplicateId);
                command.Parameters.AddWithValue(MatchingFilesScoreColumn, similarity.Score);

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await transaction.RollbackAsync(cancellationToken);
        }

        return true;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        _connection.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
            return;
        _isDisposed = true;
        await _connection.DisposeAsync();
    }
}