using Application.DTO.Boards;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class ComponentController : ControllerBase
    {
        IBoardService _boardService;
        private readonly UserManager<ApplicationUser> _userManager;
        public ComponentController(IBoardService boardService, UserManager<ApplicationUser> userManager)
        {
            _boardService = boardService;
            _userManager = userManager;
        }

        // GET: api/component
        [HttpGet]
        public IEnumerable<GetBoardDto> Get()
        {
            return _boardService.GetBoards(_userManager.GetUserId(User)!);
        }

        // GET api/component/5
        [HttpGet("{id}")]
        public ActionResult<GetBoardDto> Get(string id)
        {
            var board = _boardService.GetBoardById(_userManager.GetUserId(User)!, id);
            if (board == null)
            {
                return NotFound(new { message = "Board not found" });
            }
            return board;
        }

        // POST api/component
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public BoardResponseDto Create([FromBody] string Name)
        {
            CreateBoardDto boardDto = new CreateBoardDto() { Name = Name, Access = false };
            return _boardService.CreateBoard(_userManager.GetUserId(User)!, boardDto);
        }
    }
}
