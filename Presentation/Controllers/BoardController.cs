using Application.DTO.Boards;
using Application.Interfaces;
using Domain.Entities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public class BoardController : ControllerBase
    {
        IBoardService _boardService;
        private readonly UserManager<ApplicationUser> _userManager;
        public BoardController(IBoardService boardService, UserManager<ApplicationUser> userManager)
        {
            _boardService = boardService;
            _userManager = userManager;
        }

        // Boards belong to the user in the validated token, never to anything sent in the request.
        private string UserId => _userManager.GetUserId(User)!;

        // GET: api/board/GetBoards
        [HttpGet("GetBoards")]
        public IEnumerable<GetBoardDto> Get()
        {
            return _boardService.GetBoards(UserId);
        }

        // GET api/board/5
        [HttpGet("{id}")]
        public ActionResult<GetBoardDto> Get(string id)
        {
            var board = _boardService.GetBoardById(UserId, id);
            if (board == null)
            {
                return NotFound(new { message = "Board not found" });
            }
            return board;
        }

        // POST api/board
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public BoardResponseDto Create([FromBody] CreateBoardDto boardDto)
        {
            return _boardService.CreateBoard(UserId, boardDto);
        }

        // PUT api/board/5
        [HttpPut("{id}")]
        [IgnoreAntiforgeryToken]
        public ActionResult<BoardResponseDto> Update(string id, [FromBody] UpdateBoardDto boardDto)
        {
            var response = _boardService.UpdateBoard(UserId, id, boardDto);
            if (response == null)
            {
                return NotFound(new { message = "Board not found" });
            }
            return response;
        }
    }
}
