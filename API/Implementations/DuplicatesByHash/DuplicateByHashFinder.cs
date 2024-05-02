using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using System.Collections.Concurrent;
using API.Implementations.Common;
using Core.Entities;
using Microsoft.AspNetCore.SignalR;
using File = Core.Entities.File;

namespace API.Implementations.DuplicatesByHash
{
    public class DuplicateByHashFinder : IDuplicateByHashFinder
    {
        private readonly IFileReader _fileReader;
        private readonly IHashGenerator _hashGenerator;
        private readonly IHubContext<NotificationHub> _notificationContext;

        public DuplicateByHashFinder(IFileReader fileReader, IHashGenerator hashGenerator,
            IHubContext<NotificationHub> notificationContext)
        {
            _fileReader = fileReader;
            _hashGenerator = hashGenerator;
            _notificationContext = notificationContext;
        }

        public async Task<IEnumerable<IGrouping<string, File>>> FindDuplicateByHash(
            List<string> hypotheticalDuplicates,
            CancellationToken token)
        {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            // Get duplicates after hashing a quarter of each file
            var duplicates =
                await await GetDuplicatesForFileLength(hypotheticalDuplicates, 4, cancellationToken: token);
            
            hypotheticalDuplicates = duplicates.Where(group => group.Value.Files.Count > 1)
                .SelectMany(group => group.Value.Files.Select(file => file.Path)).ToList();

            duplicates.Clear();
            
            // Get duplicates after hashing half of each file
            duplicates =
                await await GetDuplicatesForFileLength(hypotheticalDuplicates, 2, cancellationToken: token);
            
            hypotheticalDuplicates = duplicates.Where(group => group.Value.Files.Count > 1)
                .SelectMany(group => group.Value.Files.Select(file => file.Path)).ToList();

            duplicates.Clear();
            
            // Get duplicates after hashing each file completely
            duplicates =
                await await GetDuplicatesForFileLength(hypotheticalDuplicates, 1, cancellationToken: token);
            
            return duplicates.SelectMany(group =>
                    group.Value.Files.OrderByDescending(file => file.DateModified)
                        .Select(files => new { group.Key, Value = files }))
                .ToLookup(group => group.Key, group => group.Value);
        }

        private Task<Task<ConcurrentDictionary<string, FileGroup>>> GetDuplicatesForFileLength(List<string> hypotheticalDuplicates, long lengthDivisor, CancellationToken cancellationToken)
        {
            return Task.Factory.StartNew(async () =>
            {
                var start = 0;
                var hashGenerationProgress = 0;
                var duplicates = new ConcurrentDictionary<string, FileGroup>();
                
                while (start != hypotheticalDuplicates.Count)
                {
                    var count = 2500;
                    if (start + count > hypotheticalDuplicates.Count)
                        count = hypotheticalDuplicates.Count - start;

                    await Parallel.ForEachAsync(hypotheticalDuplicates.GetRange(start, count),
                        new ParallelOptions
                            { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = cancellationToken },
                        async (path, similarityToken) =>
                        {
                            var file = new File(new FileInfo(path));

                            await using var stream = _fileReader.GetFileStream(path);
                            var hash = _hashGenerator.GenerateHash(stream, file.Size / lengthDivisor);

                            if (string.IsNullOrWhiteSpace(hash))
                            {
                                await _notificationContext.Clients.All.SendAsync("Notify",
                                    new Notification(NotificationType.Exception,
                                        $"File {file.Path} is corrupted"), cancellationToken: similarityToken);
                                return;
                            }

                            duplicates.AddOrUpdate(hash ,
                                new FileGroup(file),
                                (_, group) =>
                                {
                                    group.Files.Enqueue(file);
                                    return group;
                                });

                            Interlocked.Increment(ref hashGenerationProgress);
                            var current = Interlocked.CompareExchange(ref hashGenerationProgress, 0, 0);
                            await _notificationContext.Clients.All.SendAsync("Notify",
                                new Notification(lengthDivisor switch
                                    {
                                        4 => NotificationType.HashGenerationProgress,
                                        2 => NotificationType.SimilaritySearchProgress,
                                        _ => NotificationType.TotalProgress
                                    },
                                    current.ToString()),
                                similarityToken);
                        });
                    start += count;
                    GC.Collect();
                }

                return duplicates;
            }, cancellationToken, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }
}