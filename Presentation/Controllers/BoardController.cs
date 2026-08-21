using Application.DTO.Boards;
using Application.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class BoardController : ControllerBase
    {
        IBoardService _boardService;
        public BoardController(IBoardService boardService)
        {
            _boardService = boardService;
        }

        // GET: api/board
        [HttpGet("GetBoards")]
        public IEnumerable<GetBoardDto> Get()
        {
            return _boardService.GetBoards();
        }

        // GET api/board/5
        [HttpGet("{id}")]
        public GetBoardDto Get(string id)
        {
            return _boardService.GetBoardById(id);
        }

        // POST api/board
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public BoardResponseDto Create([FromBody] string Name)
        {
            CreateBoardDto boardDto = new CreateBoardDto() { Name = Name, Access = false };
            return _boardService.CreateBoard(boardDto);
        }
    }
}
