using System.Diagnostics;
using Core.Entities;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarAudios;
using Core.Interfaces.SimilarImages;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

[Route("api/duplicates")]
[ApiController]
public class DuplicatesController : Controller
{
    private readonly IDirectoryReader _directoryReader;
    private readonly IDuplicateByHashFinder _duplicatesByHashFinder;
    private readonly ISimilarAudiosFinder _similarAudiosFinder;
    private readonly ISimilarImagesFinder _similarImagesFinder;

    public DuplicatesController(IDirectoryReader directoryReader, ISimilarAudiosFinder similarAudiosFinder,
        ISimilarImagesFinder similarImagesFinder, IDuplicateByHashFinder duplicatesByHashFinder)
    {
        _directoryReader = directoryReader;
        _duplicatesByHashFinder = duplicatesByHashFinder;
        _similarAudiosFinder = similarAudiosFinder;
        _similarImagesFinder = similarImagesFinder;
    }

    // GET api/Duplicates/findDuplicates
    [HttpPost("findDuplicates")]
    public async Task<ActionResult> FindDuplicates([FromBody] SearchParameters searchParameters,
        CancellationToken cancellationToken = default)
    {
        if (!ModelState.IsValid) 
            return BadRequest(ModelState);
        
        var hypotheticalDuplicates = await _directoryReader.GetAllFilesFromFolderAsync(searchParameters, cancellationToken);
        
        var duplicatesGroups = searchParameters.FileSearchType switch
        {
            FileSearchType.All => await _duplicatesByHashFinder.FindDuplicateByHash(
                hypotheticalDuplicates.Keys.ToList(), cancellationToken),
            FileSearchType.Audios => await _similarAudiosFinder.FindSimilarAudiosAsync(
                hypotheticalDuplicates.Keys.ToList(), cancellationToken),
            _ => await _similarImagesFinder.FindSimilarImagesAsync(hypotheticalDuplicates,
                searchParameters.DegreeOfSimilarity, cancellationToken)
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