// using System.Data;
// using System.Data.SQLite;
// using System.Numerics.Tensors;
// using System.Runtime.CompilerServices;
// using System.Runtime.InteropServices;
// using System.Text.Json;
// using Api.Client.Repositories;
// using CommunityToolkit.HighPerformance.Buffers;
// using Core.Entities.Images;
// using Core.Entities.SearchParameters;
// using NSwag.Collections;
//
// namespace Api.Client;
//
// public sealed class SqLiteImagesRepository : ICollectionRepository, IIndexingRepository, IImageInfosRepository,
//     ISimilarImagesRepository, IDisposable, IAsyncDisposable
// {
//     private static readonly string LocalFolder =
//         Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
//
//     private static readonly string Images = Path.GetRelativePath(Environment.CurrentDirectory,
//         string.Join(Path.DirectorySeparatorChar, LocalFolder, "Europa", "images"));
//
//     private readonly string _connectionString = new SQLiteConnectionStringBuilder
//         {
//             DataSource = $"{Images}.sqlite", JournalMode = SQLiteJournalModeEnum.Wal,
//             SyncMode = SynchronizationModes.Normal, Pooling = true
//         }
//         .ConnectionString;
//
//     private readonly SQLiteConnection _connection;
//     private bool _isDisposed;
//
//     private static readonly string ImageHashColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.ImageHash));
//
//     private static readonly string ImageIdColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.FileHash));
//
//     private static readonly string ImageSimilaritiesColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(ImagesGroup.Similarities));
//
//     private static readonly string ImageIdBlobColumn = string.Join('_', ImageIdColumn, "blob");
//
//     private static readonly string SimilarityOriginalIdColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.OriginalId));
//
//     private static readonly string SimilarityDuplicateIdColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.DuplicateId));
//
//     private static readonly string SimilarityDistanceColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Similarity.Distance));
//
//     public SqLiteImagesRepository()
//     {
//         _connection = new SQLiteConnection(_connectionString);
//         _connection.Open();
//
//         switch (RuntimeInformation.OSArchitecture)
//         {
//             // Load sqlite-vec extension
//             case Architecture.X64 when RuntimeInformation.IsOSPlatform(OSPlatform.Windows):
//                 _connection.LoadExtension(string.Join(Path.DirectorySeparatorChar, Path.GetDirectoryName(
//                         System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase)[6..], "Libs",
//                     "vec0-x86_64.dll"), "sqlite3_vec_init");
//                 break;
//             case Architecture.X64 when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
//                                        RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD):
//                 _connection.LoadExtension(string.Join(Path.DirectorySeparatorChar, Path.GetDirectoryName(
//                         System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase)[6..], "Libs",
//                     "vec0-x86_64.so"), "sqlite3_vec_init");
//                 break;
//             case Architecture.X64:
//                 _connection.LoadExtension(string.Join(Path.DirectorySeparatorChar, Path.GetDirectoryName(
//                         System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase)[6..], "Libs",
//                     "vec0-x86_64.dylib"), "sqlite3_vec_init");
//                 break;
//             case Architecture.Arm64 when RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
//                                          RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD):
//                 _connection.LoadExtension(string.Join(Path.DirectorySeparatorChar, Path.GetDirectoryName(
//                         System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase)[6..], "Libs",
//                     "vec0-aarch64.so"), "sqlite3_vec_init");
//                 break;
//             case Architecture.Arm64 when RuntimeInformation.IsOSPlatform(OSPlatform.OSX):
//                 _connection.LoadExtension(string.Join(Path.DirectorySeparatorChar, Path.GetDirectoryName(
//                         System.Reflection.Assembly.GetExecutingAssembly().GetName().CodeBase)[6..], "Libs",
//                     "vec0-aarch64.dylib"), "sqlite3_vec_init");
//                 break;
//             case Architecture.X86:
//             case Architecture.Arm:
//             case Architecture.Wasm:
//             case Architecture.S390x:
//             case Architecture.LoongArch64:
//             case Architecture.Armv6:
//             case Architecture.Ppc64le:
//             case Architecture.RiscV64:
//             default:
//                 throw new ArgumentOutOfRangeException();
//         }
//     }
//
//     public async ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
//     {
//         // Create the vector table
//         await using var command = _connection.CreateCommand();
//         command.CommandText =
//             $"CREATE TABLE IF NOT EXISTS {ImageSimilaritiesColumn}({ImageSimilaritiesColumn} TEXT)";
//         await command.ExecuteNonQueryAsync(cancellationToken);
//
//         command.CommandText =
//             $"""
//              CREATE VIRTUAL TABLE IF NOT EXISTS {JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName)}
//              USING vec0({ImageIdColumn} TEXT PRIMARY KEY, {ImageHashColumn} FLOAT[256] distance_metric=l1,
//              {ImageSimilaritiesColumn} TEXT)
//              """;
//         await command.ExecuteNonQueryAsync(cancellationToken);
//     }
//
//     public async ValueTask<ImageInfos> GetImageInfos(string collectionName, byte[] id,
//         PerceptualHashAlgorithm perceptualHashAlgorithm, CancellationToken cancellationToken = default)
//     {
//         await using var command = _connection.CreateCommand();
//         var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
//
//         // Add vector first
//         command.CommandText =
//             $"SELECT {ImageHashColumn} FROM {tableName} WHERE {ImageIdColumn} = @{ImageIdColumn}";
//
//         command.Parameters.AddWithValue(ImageIdColumn, Convert.ToHexStringLower(id));
//         await using var result = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
//
//         if (!result.HasRows)
//             return new ImageInfos(Guid.Empty, Array.Empty<Half>());
//
//         await result.ReadAsync(cancellationToken);
//         var tempHash = MemoryMarshal.Cast<byte, float>(result[ImageHashColumn] as byte[]);
//         var imageHash = new Half[tempHash.Length];
//         TensorPrimitives.ConvertToHalf(tempHash, imageHash);
//         return new ImageInfos(Guid.Empty, imageHash);
//     }
//
//     [SkipLocalsInit]
//     public async ValueTask<bool> InsertImageInfos(string collectionName, List<ImagesGroup> groups,
//         PerceptualHashAlgorithm perceptualHashAlgorithm, CancellationToken cancellationToken = default)
//     {
//         await using var command = _connection.CreateCommand();
//         var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
//
//         command.CommandText =
//             $"INSERT INTO {tableName} VALUES(@{ImageIdColumn}, @{ImageHashColumn}, @{ImageSimilaritiesColumn})";
//
//         using var tempVector = MemoryOwner<float>.Allocate(groups[0].ImageHash!.Value.Length);
//         await using var transaction = _connection.BeginTransaction();
//         foreach (var group in groups)
//         {
//             var id = Convert.ToHexStringLower(group.FileHash);
//             // Insert vector
//             command.Parameters.AddWithValue(ImageIdColumn, id);
//             TensorPrimitives.ConvertToSingle(group.ImageHash!.Value.Span, tempVector.Span);
//             command.Parameters.AddWithValue(ImageHashColumn, MemoryMarshal.AsBytes(tempVector.Span).ToArray());
//             command.Parameters.AddWithValue(ImageSimilaritiesColumn, JsonSerializer.Serialize(group.Similarities));
//             await command.ExecuteNonQueryAsync(cancellationToken);
//         }
//
//         await transaction.CommitAsync(cancellationToken);
//         return true;
//     }
//
//     public ValueTask<ObservableDictionary<byte[], Similarity>?> GetExistingSimilaritiesForImage(string collectionName,
//         byte[] currentGroupId, PerceptualHashAlgorithm perceptualHashAlgorithm,
//         CancellationToken cancellationToken = default)
//     {
//         return ValueTask.FromResult<ObservableDictionary<byte[], Similarity>?>(null);
//     }
//
//     [SkipLocalsInit]
//     public async ValueTask<IEnumerable<KeyValuePair<byte[], Similarity>>> GetSimilarImages(string collectionName,
//         byte[] id, ReadOnlyMemory<Half> imageHash, PerceptualHashAlgorithm perceptualHashAlgorithm,
//         decimal degreeOfSimilarity, ICollection<byte[]> groupsAlreadyDone,
//         CancellationToken cancellationToken = default)
//     {
//         // Span<float> tempVector = stackalloc float[imageHash.Length];
//         // TensorPrimitives.ConvertToSingle(imageHash.Span, tempVector);
//         var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
//
//         await using var command = _connection.CreateCommand();
//         if (groupsAlreadyDone.Count > 0)
//             command.CommandText =
//                 $"SELECT {ImageIdColumn}, {SimilarityDistanceColumn} " +
//                 $"FROM {tableName} " +
//                 $"WHERE {ImageIdColumn} NOT IN @{ImageSimilaritiesColumn} AND {ImageHashColumn} MATCH " +
//                 $"@{ImageHashColumn} AND {SimilarityDistanceColumn} <= @{SimilarityDistanceColumn} " +
//                 "AND K = 100";
//         else
//             command.CommandText =
//                 $"""
//                  SELECT {ImageIdColumn}, distance AS {SimilarityDistanceColumn}
//                  FROM {tableName}
//                  WHERE {ImageHashColumn} MATCH @{ImageHashColumn}
//                  AND K = 100
//                  """;
//         // AND {SimilarityDistanceColumn} <= @{SimilarityDistanceColumn}
//
//         if (groupsAlreadyDone.Count > 0)
//         {
//             var ids = $"({string.Join(',', groupsAlreadyDone.Select(Convert.ToHexStringLower))})";
//
//             command.Parameters.AddWithValue(ImageSimilaritiesColumn, ids);
//         }
//
//         command.Parameters.AddWithValue(ImageHashColumn,
//             JsonSerializer.Serialize(MemoryMarshal.ToEnumerable(imageHash)));
//
//         command.Parameters.AddWithValue(SimilarityDistanceColumn, degreeOfSimilarity);
//
//         var result = await command.ExecuteReaderAsync(CommandBehavior.Default, cancellationToken);
//
//         if (!result.HasRows)
//             return [];
//
//         var matchingIds = new List<KeyValuePair<byte[], Similarity>>();
//         while (await result.ReadAsync(cancellationToken))
//         {
//             var distance = result.GetDecimal(SimilarityDistanceColumn);
//
//             if (distance > degreeOfSimilarity)
//                 continue;
//
//             var matchingId = Convert.FromHexString(result.GetString(ImageIdColumn));
//
//             if (groupsAlreadyDone.Contains(matchingId))
//                 continue;
//
//             matchingIds.Add(new KeyValuePair<byte[], Similarity>(matchingId,
//                 new Similarity
//                 {
//                     OriginalId = id, DuplicateId = matchingId, Distance = distance
//                 }));
//         }
//
//         return matchingIds;
//     }
//
//     public async ValueTask<bool> LinkToSimilarImagesAsync(string collectionName, Guid id,
//         PerceptualHashAlgorithm perceptualHashAlgorithm, ICollection<Similarity> newSimilarities,
//         CancellationToken cancellationToken = default)
//     {
//         // var tableName = JsonNamingPolicy.SnakeCaseLower.ConvertName(collectionName);
//         // await using var command = _connection.CreateCommand();
//         // command.CommandText = $"UPDATE {tableName} " +
//         //                       $"SET {ImageSimilaritiesColumn} = @{ImageSimilaritiesColumn} " +
//         //                       $"WHERE {ImageIdColumn} = @{ImageIdColumn}";
//         //
//         // command.Parameters.AddWithValue(ImageSimilaritiesColumn, JsonSerializer.Serialize(newSimilarities));
//         // command.Parameters.AddWithValue(ImageIdColumn, Convert.ToHexStringLower(id));
//         // await command.ExecuteNonQueryAsync();
//
//         return true;
//     }
//
//     public void Dispose()
//     {
//         if (_isDisposed)
//             return;
//         _connection.Dispose();
//         _isDisposed = true;
//     }
//
//     public async ValueTask DisposeAsync()
//     {
//         if (_isDisposed)
//             return;
//         await _connection.DisposeAsync();
//         _isDisposed = true;
//     }
//
//     public ValueTask DisableIndexingAsync(string collectionName, CancellationToken cancellationToken = default)
//     {
//         return ValueTask.CompletedTask;
//     }
//
//     public ValueTask EnableIndexingAsync(string collectionName, CancellationToken cancellationToken = default)
//     {
//         return ValueTask.CompletedTask;
//     }
//
//     public ValueTask<bool> IsIndexingDoneAsync(string collectionName, CancellationToken cancellationToken = default)
//     {
//         return ValueTask.FromResult(true);
//     }
// }