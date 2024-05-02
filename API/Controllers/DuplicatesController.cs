using Core.Entities;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarAudios;
using Core.Interfaces.SimilarImages;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace API.Controllers
{
    [Route("api/duplicates")]
    [ApiController]
    public class DuplicatesController : Controller
    {
        private readonly IDirectoryReader _directoryReader;
        private readonly ISimilarImagesFinder _similarImagesFinder;
        private readonly ISimilarAudiosFinder _similarAudiosFinder;
        private readonly IDuplicateByHashFinder _duplicatesByHashFinder;

        public DuplicatesController(IDirectoryReader directoryReader, ISimilarAudiosFinder similarAudiosFinder, ISimilarImagesFinder similarImagesFinder, IDuplicateByHashFinder duplicatesByHashFinder)
        {
            _directoryReader = directoryReader;
            _duplicatesByHashFinder = duplicatesByHashFinder;
            _similarAudiosFinder = similarAudiosFinder;
            _similarImagesFinder = similarImagesFinder;
        }

        // GET api/Duplicates/findDuplicates
        [HttpPost("findDuplicates")]
        public async Task<ActionResult> FindDuplicates([FromBody] SearchParameters searchParameters, CancellationToken token = default)
        {
            if (ModelState.IsValid)
            {
                var hypotheticalDuplicates = await _directoryReader.GetAllFilesFromFolderAsync(searchParameters, token);
                
                var duplicatesGroups = searchParameters.FileSearchType switch
                {
                    FileSearchType.All => await _duplicatesByHashFinder.FindDuplicateByHash(
                        hypotheticalDuplicates, token),
                    FileSearchType.Audios => await _similarAudiosFinder.FindSimilarAudiosAsync(
                        hypotheticalDuplicates, token),
                    _ => await _similarImagesFinder.FindSimilarImagesAsync(hypotheticalDuplicates,
                        searchParameters.DegreeOfSimilarity, token)
                };

                return Ok(duplicatesGroups.ToResponseDto());
            }

            return BadRequest(ModelState);
        }

        // GET api/Commons/openFileLocation
        [HttpGet("openFileLocation")]
        public ActionResult OpenFileLocation([FromQuery] FileDto request)
        {
            try
            {
                if (_directoryReader.FileExists(request.Path))
                {
                    Process.Start("explorer.exe", "/select, " + Path.GetFullPath(request.Path));
                    return StatusCode(200);
                }
                return StatusCode(404, $"The file {request.Path} does not exist.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }
    }
}
