namespace Database.Interfaces
{
    public interface IDbHelpers
    {
        void InsertImageHash(string hash, int position, string path);
        IReadOnlyCollection<int> GetSimilarHashIndex(string hash, int position, int limit);
    }
}
