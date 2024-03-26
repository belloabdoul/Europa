using System.ComponentModel.DataAnnotations;

namespace Core.Entities
{
    public class RequestDuplicates
    {
        [Required]
        public List<string> Folders { get; set; }
        public SearchParametersDto SearchParameters { get; set; } = new SearchParametersDto();
    }
}
