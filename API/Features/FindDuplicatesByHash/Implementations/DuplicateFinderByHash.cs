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

        public IEnumerable<IGrouping<string, File>> FindDuplicateByHash(List<string> hypotheticalDuplicates, CancellationToken token, out List<string> errors)
        {
            errors = [];
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var duplicates = new ConcurrentBag<File>();

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = token
            };

            Parallel.ForEach(Partitioner.Create(0, hypotheticalDuplicates.Count), options, range =>
            {
                for (int current = range.Item1; current < range.Item2; current++)
                {
                    duplicates.Add(new File(new FileInfo(hypotheticalDuplicates[current]), _hashGenerator.GenerateHash(hypotheticalDuplicates[current])));
                }
            });

            return duplicates.OrderByDescending(file => file.DateModified).GroupBy(file => file.FinalHash).Where(i => i.Count() != 1);
        }
    }
}
