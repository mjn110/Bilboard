using Application.DTO.Boards;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Services
{
    public class BoardService : Interfaces.IBoardService
    {
        public IEnumerable<GetBoardDto> GetBoards()
        {
            return new GetBoardDto[] { new GetBoardDto { BoardName = "Board1" }, new GetBoardDto { BoardName = "Board2" } };
        }

        public GetBoardDto GetBoardById(int id)
        {
            return new GetBoardDto { BoardName = "Board1" };
        }

        public createBoardResponseDto CreateBoard(CreateBoardDto boardDto)
        {
            return new createBoardResponseDto
            {
                Response = "Here is " + boardDto.Request + " data",
                Date = DateTime.Now
            };
        }
    }
}
