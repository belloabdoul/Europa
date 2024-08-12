using Core.Entities;

namespace Core.Interfaces;

public interface ISearchTypeImplementationFactory
{
    ISimilarFilesFinder GetSearchImplementation(FileSearchType searchType, int degreeOfSimilarity = 0);
}