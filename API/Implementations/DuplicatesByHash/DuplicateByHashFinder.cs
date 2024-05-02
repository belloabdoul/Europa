using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using System.Collections.Concurrent;
using API.Implementations.Common;
using Blake3;
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

        public DuplicateByHashFinder(IFileReader fileReader, IHashGenerator hashGenerator, IHubContext<NotificationHub> notificationContext)
        {
            _fileReader = fileReader;
            _hashGenerator = hashGenerator;
            _notificationContext = notificationContext;
        }

        public async Task<IEnumerable<IGrouping<string, File>>> FindDuplicateByHash(List<string> hypotheticalDuplicates, CancellationToken token)
        {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var duplicates = new ConcurrentQueue<File>();

            var passDuplicates = new ConcurrentQueue<string>();

            var hashGenerationProgress = 0;

            // for (int i = 0; i < 4; i++)
            // {
                await Task.Factory.StartNew(() =>
                {
                    hypotheticalDuplicates
                        .AsParallel()
                        .WithDegreeOfParallelism(Environment.ProcessorCount)
                        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                        .WithCancellation(token)
                        .ForAll(async file =>
                        {
                            var fileInfo = new FileInfo(file);

                            // if(fileInfo.Length > 1073741824)
                            using var stream = _fileReader.GetFileStream(file);
                            // duplicates.Enqueue(new File(fileInfo, _hashGenerator.GenerateHash(stream, fileInfo.Length)));
                            
                            Interlocked.Increment(ref hashGenerationProgress);
                            var current = Interlocked.CompareExchange(ref hashGenerationProgress, 0, 0);
                            await _notificationContext.Clients.All.SendAsync("Notify",
                                new Notification(NotificationType.HashGenerationProgress,
                                    current.ToString()),
                                token);
                        });
                }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            // }

            return duplicates.OrderByDescending(file => file.DateModified).GroupBy(file => file.Id).Where(i => i.Count() != 1);
        }
    }
}
