namespace API.Common.Entities
{
    public class RequestDuplicateFor
    {
        public string File { get; set; }
        public List<string> Folders { get; set; }
        public SearchParametersDto SearchParameters { get; set; } = new SearchParametersDto();
    }
}
