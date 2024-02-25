using API.Common.Entities;
using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Entities;
using API.Features.FindDuplicatesByHash.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace API.Features.FindDuplicatesByHash.Controllers
{
    public class DuplicatesController : Controller
    {
        private readonly IDirectoryReader _directoryReader;
        private readonly IDuplicateFinderByHash _duplicateFinder;

        public DuplicatesController(IDirectoryReader directoryReader, IDuplicateFinderByHash duplicateFinder)
        {
            _directoryReader = directoryReader;
            _duplicateFinder = duplicateFinder;
        }

        // POST api/Duplicates/findDuplicatesByHash
        [HttpGet("findDuplicatesByHash")]
        public ActionResult FindDuplicateByHash([FromQuery] RequestDuplicates request, CancellationToken token)
        {
            List<string> hypotheticalDuplicates = [];

            request.SearchParameters.FilesTypeToSearch = 0;

            hypotheticalDuplicates.AddRange(_directoryReader.GetAllFilesFromFolder(request.Folders, request.SearchParameters, token, out var readerErrors));

            var duplicates = _duplicateFinder.FindDuplicateByHash(hypotheticalDuplicates.Distinct().ToList(), token, out var hasherErrors);

            return StatusCode(200, duplicates.ToResponse([.. readerErrors, .. hasherErrors]));
        }
    }
}
