using API.Common.Entities;
using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Interfaces;
using API.Features.FindSimilarAudios.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace API.Features.FindSimilarAudios.Controllers
{
    public class AudiosController : Controller
    {
        private readonly IDirectoryReader _directoryReader;
        private readonly ISimilarAudiosFinder _similarAudiosFinder;

        public AudiosController(IDirectoryReader directoryReader, ISimilarAudiosFinder similarAudiosFinder)
        {
            _directoryReader = directoryReader;
            _similarAudiosFinder = similarAudiosFinder;
        }

        // POST api/Audios/findSimilarAudios
        [HttpGet("findSimilarAudios")]
        public async Task<ActionResult> FindSimilarAudios([FromQuery] RequestDuplicates request, CancellationToken token)
        {
            List<string> hypotheticalDuplicates = [];

            request.SearchParameters.FilesTypeToSearch = (FileType)2;

            hypotheticalDuplicates.AddRange(_directoryReader.GetAllFilesFromFolder(request.Folders, request.SearchParameters, token, out var readerErrors));

            var duplicates = await _similarAudiosFinder.FindSimilarAudiosAsync(hypotheticalDuplicates.Distinct().ToList(), token);

            return StatusCode(200, duplicates);
        }
    }
}
