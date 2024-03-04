using API.Features.FindDuplicatesByHash.Interfaces;
using System.Collections.Concurrent;
using File = API.Common.Entities.File;

namespace API.Features.FindDuplicatesByHash.Implementations
{
    public class DuplicateFinderByHash : IDuplicateFinderByHash
    {
        private readonly IHashGenerator _hashGenerator;

        public DuplicateFinderByHash(IHashGenerator hashGenerator)
        {
            _hashGenerator = hashGenerator;
        }

        public IEnumerable<IGrouping<string, File>> FindDuplicateByHash(List<string> hypotheticalDuplicates, CancellationToken token)
        {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var duplicates = new ConcurrentBag<File>();

            hypotheticalDuplicates
                .AsParallel()
                .WithDegreeOfParallelism((int)(Environment.ProcessorCount * 0.9))
                .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
                .WithCancellation(token)
                .ForAll(file =>
                {
                    duplicates.Add(new File(new FileInfo(file), _hashGenerator.GenerateHash(file)));
                });

            return duplicates.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash).Where(i => i.Count() != 1);
        }
    }
}
