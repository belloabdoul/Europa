using System.ComponentModel.DataAnnotations;

namespace API.Common.Entities
{
    public class RequestDuplicates
    {
        [Required]
        public List<string> Folders { get; set; }
        public SearchParametersDto SearchParameters { get; set; } = new SearchParametersDto();
    }
}
