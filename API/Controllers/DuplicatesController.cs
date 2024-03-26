﻿using Core.Entities;
using Core.Interfaces.Common;
using Core.Interfaces.DuplicatesByHash;
using Core.Interfaces.SimilarAudios;
using Core.Interfaces.SimilarImages;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using File = Core.Entities.File;

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
        public async Task<ActionResult> FindDuplicates([FromBody] RequestDuplicates request, CancellationToken token = default)
        {
            if (ModelState.IsValid)
            {
                IEnumerable<IGrouping<string, File>> duplicatesGroups;

                var hypotheticalDuplicates = await _directoryReader.GetAllFilesFromFolderAsync(request.Folders.Distinct().ToList(), request.SearchParameters, token);

                if (request.SearchParameters.FileTypeToSearch == FileType.All)
                    duplicatesGroups = await _duplicatesByHashFinder.FindDuplicateByHash(hypotheticalDuplicates.Distinct().ToList(), token);
                else if (request.SearchParameters.FileTypeToSearch == FileType.Audios)
                    duplicatesGroups = await _similarAudiosFinder.FindSimilarAudiosAsync(hypotheticalDuplicates.Distinct().ToList(), token);
                else
                    duplicatesGroups = await _similarImagesFinder.FindSimilarImagesAsync(hypotheticalDuplicates.Distinct().ToList(), request.Folders, token);

                return Ok(duplicatesGroups.ToResponseDTO());
            }
            else
            {
                return BadRequest(ModelState);
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
