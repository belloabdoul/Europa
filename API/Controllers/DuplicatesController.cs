using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.HighPerformance.Buffers;
using Core.Entities;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarAudios;
using Core.Interfaces.SimilarImages;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/duplicates")]
[ApiController]
public class DuplicatesController : Controller
{
    private readonly IValidator<SearchParameters> _searchParametersValidator;
    private readonly IDirectoryReader _directoryReader;
    private readonly IDuplicateByHashFinder _duplicatesByHashFinder;
    private readonly ISimilarAudiosFinder _similarAudiosFinder;
    private readonly ISimilarImagesFinder _similarImagesFinder;


    public DuplicatesController(IValidator<SearchParameters> searchParametersValidator, IDirectoryReader directoryReader, ISimilarAudiosFinder similarAudiosFinder,
        ISimilarImagesFinder similarImagesFinder, IDuplicateByHashFinder duplicatesByHashFinder)
    {
        _searchParametersValidator = searchParametersValidator;
        _directoryReader = directoryReader;
        _duplicatesByHashFinder = duplicatesByHashFinder;
        _similarAudiosFinder = similarAudiosFinder;
        _similarImagesFinder = similarImagesFinder;
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

        var hypotheticalDuplicates = await _directoryReader.GetAllFilesFromFolderAsync(searchParameters, cancellationToken);
        
        GC.Collect();
        
        var duplicatesGroups = searchParameters.FileSearchType switch
        {
            FileSearchType.All => await _duplicatesByHashFinder.FindDuplicateByHash(
                hypotheticalDuplicates, cancellationToken),
            FileSearchType.Audios => await _similarAudiosFinder.FindSimilarAudiosAsync(
                hypotheticalDuplicates, cancellationToken),
            FileSearchType.Images => await _similarImagesFinder.FindSimilarImagesAsync(hypotheticalDuplicates,
                searchParameters.DegreeOfSimilarity!.Value, cancellationToken)
        };
        
        return Ok(duplicatesGroups.ToResponseDto());
    }

    // GET api/Commons/openFileLocation
    [HttpGet("openFileLocation")]
    public ActionResult OpenFileLocation([FromQuery] FileDto request)
    {
        try
        {
            if (!_directoryReader.FileExists(request.Path))
                return StatusCode(404, $"The file {request.Path} does not exist.");
            Process.Start("explorer.exe", "/select, " + Path.GetFullPath(request.Path));
            return StatusCode(200);
        }
        catch (Exception ex)
        {
            return StatusCode(500, ex.Message);
        }
    }
}