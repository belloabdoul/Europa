using API.Common.Entities;
using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Entities;
using API.Features.FindSimilarImages.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace API.Features.FindSimilarImages.Controllers
{
    public class ImagesController : Controller
    {
        private readonly IDirectoryReader _directoryReader;
        private readonly ISimilarImagesFinder _similarImagesFinder;

        public ImagesController(IDirectoryReader directoryReader, ISimilarImagesFinder similarImagesFinder)
        {
            _directoryReader = directoryReader;
            _similarImagesFinder = similarImagesFinder;
        }

        // POST api/Images/findSimilarImages
        [HttpGet("findSimilarImages")]
        public async Task<ActionResult> FindSimilarImages([FromQuery] RequestDuplicates request, CancellationToken token)
        {
            List<string> hypotheticalDuplicates = [];

            request.SearchParameters.FilesTypeToSearch = (FileType)1;

            hypotheticalDuplicates.AddRange(_directoryReader.GetAllFilesFromFolder(request.Folders, request.SearchParameters, token, out var readerErrors));

            var (duplicates, hasherErrors) = await _similarImagesFinder.FindSimilarImagesAsync(hypotheticalDuplicates.Distinct().ToList(), token);

            //var hash = _similarImagesFinder.FindSimilarImages(hypotheticalDuplicates.Distinct().ToList(), token);

            return StatusCode(200, duplicates.ToResponse([.. readerErrors, .. hasherErrors]));
        }
    }
}
