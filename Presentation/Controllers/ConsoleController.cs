using Application.DTO.Boards;
using Application.Interfaces;
using Bilboard.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ConsoleController : ControllerBase
    {
        IConsoleService _consoleService;
        public ConsoleController(IConsoleService consoleService)
        { 
            _consoleService = consoleService;
        }

        // GET: api/console
        [HttpPost]
        public void WriteYellow(string message)
        {
            _consoleService.WriteLineYellow(message);
        }

        [HttpPost]
        public void WriteGreen(string message)
        {
            _consoleService.WriteLineGreen(message);
        }

        [HttpPost]
        public void WriteCyan(string message)
        {   
            _consoleService.WriteLineCyan(message);
        }
    }
}