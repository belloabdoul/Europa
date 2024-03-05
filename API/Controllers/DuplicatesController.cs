﻿using API.Common.Entities;
using API.Common.Interfaces;
using API.Features.FindDuplicatesByHash.Interfaces;
using API.Features.FindSimilarAudios.Interfaces;
using API.Features.FindSimilarImages.Interfaces;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using File = API.Common.Entities.File;

namespace API.Controllers
{
    public class DuplicatesController : Controller
    {
        private readonly IDirectoryReader _directoryReader;
        private readonly ISimilarImagesFinder _similarImagesFinder;
        private readonly ISimilarAudiosFinder _similarAudiosFinder;
        private readonly IDuplicateFinderByHash _duplicatesByHashFinder;

        public DuplicatesController(IDirectoryReader directoryReader, ISimilarAudiosFinder similarAudiosFinder, ISimilarImagesFinder similarImagesFinder, IDuplicateFinderByHash duplicatesByHashFinder)
        {
            _directoryReader = directoryReader;
            _duplicatesByHashFinder = duplicatesByHashFinder;
            _similarAudiosFinder = similarAudiosFinder;
            _similarImagesFinder = similarImagesFinder;
        }

        // GET api/Duplicates/findDuplicates
        [HttpGet("findDuplicates")]
        public async Task<ActionResult> FindDuplicates([FromQuery] RequestDuplicates request, CancellationToken token)
        {
            if (ModelState.IsValid)
            {
                var hypotheticalDuplicates = _directoryReader.GetAllFilesFromFolder(request.Folders, request.SearchParameters, token, out var readerErrors);
                var exceptions = new List<string>();
                IEnumerable<IGrouping<string, File>> duplicatesGroups;
                if (request.SearchParameters.FileTypeToSearch == FileType.All)
                    duplicatesGroups = await _duplicatesByHashFinder.FindDuplicateByHash(hypotheticalDuplicates.Distinct().ToList(), token);
                else if (request.SearchParameters.FileTypeToSearch == FileType.Audios)
                    duplicatesGroups = await _similarAudiosFinder.FindSimilarAudiosAsync(hypotheticalDuplicates.Distinct().ToList(), token);
                else
                    (duplicatesGroups, exceptions) = await _similarImagesFinder.FindSimilarImagesAsync(hypotheticalDuplicates.Distinct().ToList(), token);

                return Ok(duplicatesGroups.ToResponse([.. readerErrors, .. exceptions]));
            }
            else
            {
                return UnprocessableEntity(ModelState);
            }
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
