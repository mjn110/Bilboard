using Application.DTO.Boards;
using Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Presentation.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ComponentController : ControllerBase
    {
        IBoardService _boardService;
        public ComponentController(IBoardService boardService)
        { 
            _boardService = boardService;
        }

        // GET: api/component
        [HttpGet]
        public IEnumerable<GetBoardDto> Get()
        {
            return _boardService.GetBoards();
        }

        // GET api/component/5
        [HttpGet("{id}")]
        public GetBoardDto Get(int id)
        {
            return _boardService.GetBoardById(id);
        }

        // POST api/component
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public createBoardResponseDto Create([FromBody] string value)
        {
            CreateBoardDto boardDto = new CreateBoardDto() { Request = value };
            return _boardService.CreateBoard(boardDto);
        }
    }
}
