using API.Entities;
using System.Collections.Concurrent;

namespace API.Features.FindDuplicatesByHash
{
    public class DuplicateFinderByHash : IDuplicateFinderByHash
    {
        private readonly IHashGenerator _hashGenerator;

        public DuplicateFinderByHash(IHashGenerator hashGenerator)
        {
            _hashGenerator = hashGenerator;
        }

        public async Task<IEnumerable<IGrouping<string, FileDto>>> FindDuplicateByHashAsync(List<string> hypotheticalDuplicates, CancellationToken token)
        {
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;

            var duplicates = new ConcurrentBag<FileDto>();

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = token
            };

            token.ThrowIfCancellationRequested();

            Parallel.ForEach(Partitioner.Create(0, hypotheticalDuplicates.Count), options, range =>
            {
                for (int current = range.Item1; current < range.Item2; current++)
                {
                    duplicates.Add(new FileDto(new FileInfo(hypotheticalDuplicates[current]), _hashGenerator.GenerateHash(hypotheticalDuplicates[current])));
                    Console.WriteLine(current);
                }
            });

            token.ThrowIfCancellationRequested();

            token.ThrowIfCancellationRequested();
            return duplicates.OrderByDescending(file => file.DateModified).GroupBy(file => file.Hash).Where(i => i.Count() != 1);
        }
    }
}
