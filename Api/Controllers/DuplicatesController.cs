using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("api/duplicates")]
[ApiController]
public class DuplicatesController : Controller
{
    private readonly IDirectoryReader _directoryReader;
    private readonly IValidator<SearchParameters> _searchParametersValidator;
    private readonly ISearchService _searchService;


    public DuplicatesController(IValidator<SearchParameters> searchParametersValidator,
        IDirectoryReader directoryReader, ISearchService searchService)
    {
        _searchParametersValidator = searchParametersValidator;
        _directoryReader = directoryReader;
        _searchService = searchService;
    }

    // GET api/Duplicates/findDuplicates
    [HttpPost("findDuplicates")]
    public async Task<ActionResult> FindDuplicates(SearchParameters searchParameters,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _searchParametersValidator.ValidateAsync(searchParameters, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.ToDictionary());
        }

        var hypotheticalDuplicates =
            await _directoryReader.GetAllFilesFromFolderAsync(searchParameters, cancellationToken);

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);

        var duplicatesGroups = await _searchService.SearchAsync(hypotheticalDuplicates,
            searchParameters.FileSearchType!.Value,
            searchParameters.PerceptualHashAlgorithm ?? PerceptualHashAlgorithm.PerceptualHash,
            searchParameters.DegreeOfSimilarity ?? 0, cancellationToken);

        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
            
        return Ok(duplicatesGroups.ToResponseDto());
    }
}