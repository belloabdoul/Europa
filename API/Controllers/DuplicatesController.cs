using System.Diagnostics;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Core.Interfaces;
using Core.Interfaces.Common;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/duplicates")]
[ApiController]
public class DuplicatesController : Controller
{
    private readonly IDirectoryReader _directoryReader;
    private readonly IValidator<SearchParameters> _searchParametersValidator;
    private readonly ISearchTypeImplementationFactory _searchTypeImplementationFactory;


    public DuplicatesController(IValidator<SearchParameters> searchParametersValidator,
        IDirectoryReader directoryReader, ISearchTypeImplementationFactory searchTypeImplementationFactory)
    {
        _searchParametersValidator = searchParametersValidator;
        _directoryReader = directoryReader;
        _searchTypeImplementationFactory = searchTypeImplementationFactory;
    }

    // GET api/Duplicates/findDuplicates
    [HttpPost("findDuplicates")]
    public async Task<ActionResult> FindDuplicates(SearchParameters searchParameters,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await _searchParametersValidator.ValidateAsync(searchParameters, cancellationToken);

        StringPool.Shared.Reset();

        if (!validationResult.IsValid)
        {
            validationResult.AddToModelState(ModelState);
            return BadRequest(ModelState);
        }

        var hypotheticalDuplicates =
            await _directoryReader.GetAllFilesFromFolderAsync(searchParameters, cancellationToken);

        var searchImplementation =
            _searchTypeImplementationFactory.GetSearchImplementation(searchParameters.FileSearchType!.Value,
                searchParameters.DegreeOfSimilarity ?? 0);

        GC.Collect(2, GCCollectionMode.Default, true, true);

        var duplicatesGroups =
            await searchImplementation.FindSimilarFilesAsync(hypotheticalDuplicates, cancellationToken);

        return Ok(duplicatesGroups.ToResponseDto());
    }
}