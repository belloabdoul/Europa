using System.ComponentModel.DataAnnotations;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace Core.Entities
{
    public class Similarity
    {
        [StringLength(64)]
        public string HypotheticalOriginalId { get; set; }
        [StringLength(64)]
        public string HypotheticalDuplicateId { get; init; }
        public File HypotheticalOriginal { get; init; }
        public File HypotheticalDuplicate { get; init; }
        public double Score { get; init; }
        public Similarity() { }

        public Similarity(string hypotheticalOriginalId, string hypotheticalDuplicateId, double score)
        {
            HypotheticalOriginalId = hypotheticalOriginalId;
            HypotheticalDuplicateId = hypotheticalDuplicateId;
            Score = score;
        }
    }
}
