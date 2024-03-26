using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using System.Collections.Concurrent;
using File = Core.Entities.File;

namespace API.Implementations.DuplicatesByHash
{
    public class DuplicateByHashFinder : IDuplicateByHashFinder
    {
        private readonly IFileReader _fileReader;
        private readonly IHashGenerator _hashGenerator;

        public DuplicateByHashFinder(IFileReader fileReader, IHashGenerator hashGenerator)
        {
            _fileReader = fileReader;
            _hashGenerator = hashGenerator;
        }

        public async Task<IEnumerable<IGrouping<string, File>>> FindDuplicateByHash(List<string> hypotheticalDuplicates, CancellationToken token)
        {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var duplicates = new ConcurrentBag<File>();

            await Task.Factory.StartNew(() =>
            {
                hypotheticalDuplicates
                .AsParallel()
                .WithDegreeOfParallelism((int)(Environment.ProcessorCount * 0.9))
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .WithCancellation(token)
                .ForAll(file =>
                {
                    using var fileStream = _fileReader.GetFileStream(file);
                    duplicates.Add(new File(new FileInfo(file), _hashGenerator.GenerateHash(fileStream)));
                });
            }, token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            return duplicates.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash).Where(i => i.Count() != 1);
        }
    }
}
