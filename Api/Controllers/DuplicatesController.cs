using Core.Entities.Files;
using Core.Entities.SearchParameters;
using Core.Interfaces.Commons;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

[Route("duplicates")]
[ApiController]
public class DuplicatesController(
    IValidator<SearchParameters> searchParametersValidator,
    IDirectoryReader directoryReader,
    ISearchService searchService)
    : Controller
{
    // GET api/Duplicates/findDuplicates
    [HttpPost("findDuplicates")]
    public async Task<ActionResult> FindDuplicates(SearchParameters searchParameters,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await searchParametersValidator.ValidateAsync(searchParameters, cancellationToken);

        if (!validationResult.IsValid)
        {
            return BadRequest(validationResult.ToDictionary());
        }

        var hypotheticalDuplicates =
            await directoryReader.GetAllFilesFromFolderAsync(searchParameters, cancellationToken);
        
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        
        var duplicatesGroups = await searchService.SearchAsync(hypotheticalDuplicates,
            searchParameters.FileSearchType!.Value,
            searchParameters.DegreeOfSimilarity ?? 0, cancellationToken);
        
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);

        return Ok(duplicatesGroups.ToResponseDto());
    }
}