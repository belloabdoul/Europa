namespace Api.Client.Repositories;

public interface ICollectionRepository
{
    /// <summary>
    /// Create the tables needed for this repository's implementation
    /// </summary>
    /// <param name="cancellationToken">cancellationToken — An optional token to cancel the asynchronous operation. The default value is None.</param>
    /// <returns></returns>
    Task CreateTablesAsync(CancellationToken cancellationToken = default);
}