using API.Common.Entities;
using API.Common.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace API.Common.Controllers
{
    public class CommonsController : Controller
    {
        private readonly IDirectoryReader _directoryReader;

        public CommonsController(IDirectoryReader directoryReader)
        {
            _directoryReader = directoryReader;
        }

        // POST api/Commons/openFileLocation
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
