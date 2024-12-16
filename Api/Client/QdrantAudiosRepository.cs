// using System.Collections;
// using System.Collections.Concurrent;
// using System.Diagnostics.CodeAnalysis;
// using System.Runtime.InteropServices;
// using System.Text.Json;
// using Api.Client.Repositories;
// using Core.Entities.Audios;
// using Core.Entities.Commons;
// using Core.Entities.Images;
// using DotNext.Collections.Generic;
// using Google.Protobuf.Collections;
// using Qdrant.Client;
// using Qdrant.Client.Grpc;
// using Fingerprint = Core.Entities.Audios.Fingerprint;
// using static Qdrant.Client.Grpc.Conditions;
// using Range = Qdrant.Client.Grpc.Range;
//
// namespace Api.Client;
//
// public sealed class QdrantAudiosRepository : ICollectionRepository, IAudioInfosRepository,
//     ISimilarAudiosRepository, IDisposable
// {
//     private readonly QdrantClient _database = new("localhost");
//     private static readonly HashComparer HashComparer = new();
//     private bool _isDisposed;
//
//     private static readonly string FingerprintFileHashColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.FileHash));
//
//     private static readonly string FingerprintStartAtColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.StartAt));
//
//     private static readonly string FingerprintHashColumn =
//         JsonNamingPolicy.SnakeCaseLower.ConvertName(nameof(Fingerprint.HashBins));
//
//     public async ValueTask CreateCollectionAsync(string collectionName, CancellationToken cancellationToken = default)
//     {
//         VectorParamsMap? vectorConfig = null;
//         if (!await _database.CollectionExistsAsync(collectionName, cancellationToken))
//         {
//             await _database.CreateCollectionAsync(collectionName, vectorConfig, onDiskPayload: true,
//                 cancellationToken: cancellationToken);
//
//             await _database.CreatePayloadIndexAsync(collectionName, FingerprintFileHashColumn,
//                 cancellationToken: cancellationToken);
//
//             await _database.CreatePayloadIndexAsync(collectionName, FingerprintStartAtColumn, PayloadSchemaType.Float,
//                 cancellationToken: cancellationToken);
//
//             for (var i = 0; i < Fingerprint.BucketCount; i++)
//             {
//                 await _database.CreatePayloadIndexAsync(collectionName, $"{FingerprintHashColumn}.{i}",
//                     PayloadSchemaType.Integer,
//                     cancellationToken: cancellationToken);
//             }
//         }
//     }
//
//     public async ValueTask<bool> IsAlreadyInsertedAsync(string collectionName, byte[] id,
//         int estimatedNumberOfFingerprints, CancellationToken cancellationToken = default)
//     {
//         var count = await _database.CountAsync(collectionName,
//             filter: MatchKeyword(FingerprintFileHashColumn, Convert.ToHexStringLower(id)),
//             exact: false, cancellationToken: cancellationToken);
//
//         return count > 0;
//     }
//
//     public async ValueTask<bool> InsertFingerprintsAsync(string collectionName, List<Fingerprint> fingerprints,
//         CancellationToken cancellationToken = default)
//     {
//         var points = new List<PointStruct>(1000);
//         var id = Convert.ToHexStringLower(fingerprints[0].FileHash);
//         foreach (var fingerprintBatch in fingerprints.Chunk(1000))
//         {
//             points.AddRange(fingerprintBatch.Select(fingerprint =>
//             {
//                 return new PointStruct
//                 {
//                     Id = Guid.CreateVersion7(),
//                     Payload =
//                     {
//                         [FingerprintFileHashColumn] = id, [FingerprintStartAtColumn] = fingerprint.StartAt,
//                         [FingerprintHashColumn] = new Value
//                         {
//                             StructValue = new Struct
//                             {
//                                 Fields =
//                                 {
//                                     fingerprint.HashBins
//                                         .Index().Select(val =>
//                                             new KeyValuePair<string, Value>($"{val.Index}",
//                                                 new Value { IntegerValue = val.Item }))
//                                         .ToDictionary(val => val.Key, val => val.Value)
//                                 }
//                             }
//                         }
//                     },
//                     Vectors = new Vectors { Vectors_ = new NamedVectors() }
//                 };
//             }));
//             await _database.UpsertAsync(collectionName, points, cancellationToken: cancellationToken);
//             points.Clear();
//         }
//
//         return true;
//     }
//
//     public ValueTask<ConcurrentDictionary<byte[], FingerprintMatch>?> GetExistingMatchesForFingerPrintAsync(
//         string collectionName, byte[] fileId, int index, CancellationToken cancellationToken = default)
//     {
//         throw new NotImplementedException();
//     }
//
//     public async ValueTask<IEnumerable<KeyValuePair<byte[], double>>> GetMatchingFingerprintsAsync(
//         string collectionName, Fingerprint fingerprint, byte thresholdVotes, float gapAllowed,
//         ICollection<byte[]> groupsAlreadyDone, CancellationToken cancellationToken = default)
//     {
//         var matchingFingerprints = new Dictionary<Guid, Fingerprint>();
//
//         for (var i = 0; i < Fingerprint.BucketCount; i++)
//         {
//             var results = await _database.QueryAsync(collectionName, filter: new Filter
//             {
//                 Must =
//                 {
//                     new[]
//                     {
//                         Match($"{FingerprintHashColumn}.{i}", fingerprint.HashBins[i]),
//                         Range(FingerprintStartAtColumn,
//                             new Range
//                             {
//                                 Gte = fingerprint.StartAt - gapAllowed, Lte = fingerprint.StartAt + gapAllowed
//                             })
//                     }
//                 }
//             }, limit: long.MaxValue, payloadSelector: new WithPayloadSelector
//             {
//                 Enable = true,
//                 Include = new PayloadIncludeSelector
//                     { Fields = { FingerprintFileHashColumn, FingerprintStartAtColumn } }
//             }, cancellationToken: cancellationToken);
//
//             foreach (var point in results)
//             {
//                 var duplicateId = Convert.FromHexString(point.Payload[FingerprintFileHashColumn].StringValue);
//                 var match = matchingFingerprints.GetOrAdd(Guid.Parse(point.Id.Uuid), new Fingerprint());
//                 if (match.FileHash.Length == 0)
//                 {
//                     match.FileHash = duplicateId;
//                     match.StartAt = point.Payload[FingerprintStartAtColumn].DoubleValue;
//                 }
//
//                 match.Score++;
//             }
//         }
//
//         return matchingFingerprints.Values.Where(matchingFingerprint => matchingFingerprint.Score >= thresholdVotes)
//             .Select(matchingFingerprint =>
//                 new KeyValuePair<byte[], double>(matchingFingerprint.FileHash, matchingFingerprint.StartAt));
//     }
//
//     public ValueTask<bool> LinkToSimilarFingerprintsAsync(string collectionName, byte[] id,
//         ICollection<FingerprintMatch> newSimilarities, CancellationToken cancellationToken = default)
//     {
//         throw new NotImplementedException();
//     }
//
//
//     // public async ValueTask<ConcurrentDictionary<byte[], FingerprintMatch>?> GetExistingMatchesForFingerPrint(
//     //     byte[] fileId, int index)
//     // {
//     //     var points = await _database.QueryAsync("Europa",
//     //         filter: new Filter
//     //         {
//     //             Must =
//     //             {
//     //                 MatchKeyword(nameof(AudiosGroup.Id), Convert.ToHexStringLower(fileId)),
//     //                 Qdrant.Client.Grpc.Match(nameof(SubFingerprint.Index), index)
//     //             }
//     //         },
//     //         limit: 1,
//     //         payloadSelector: new WithPayloadSelector
//     //         {
//     //             Enable = true,
//     //             Include = new PayloadIncludeSelector
//     //             {
//     //                 Fields =
//     //                 {
//     //                     $"{nameof(AudiosGroup.Matches)}[].{nameof(FingerprintMatch.DuplicateId)}",
//     //                     $"{nameof(AudiosGroup.Matches)}[].{nameof(FingerprintMatch.Score)}",
//     //                     $"{nameof(AudiosGroup.Matches)}[].{nameof(FingerprintMatch.Gap)}"
//     //                 }
//     //             }
//     //         });
//     //
//     //     if (points.Count != 1)
//     //         return null;
//     //
//     //     return new ConcurrentDictionary<byte[], FingerprintMatch>(points[0]
//     //         .Payload[nameof(AudiosGroup.Matches)].ListValue
//     //         .Values
//     //         .Select(value =>
//     //             new FingerprintMatch
//     //             {
//     //                 OriginalId = fileId,
//     //                 DuplicateId =
//     //                     Convert.FromHexString(
//     //                         value.StructValue.Fields[nameof(FingerprintMatch.DuplicateId)].StringValue),
//     //                 Score = value.StructValue.Fields[nameof(FingerprintMatch.Score)].IntegerValue,
//     //                 Gap = value.StructValue.Fields[nameof(FingerprintMatch.Score)].IntegerValue
//     //             })
//     //         .ToDictionary(val => val.DuplicateId, val => val, HashComparer), HashComparer);
//     // }
//
//     public void Dispose()
//     {
//         if (_isDisposed)
//             return;
//         _database.Dispose();
//         _isDisposed = true;
//     }
// }