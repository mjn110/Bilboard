using Application.DTO.Boards;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interfaces
{
    public interface IBoardService
    {
        public IEnumerable<GetBoardDto> GetBoards();

        public GetBoardDto GetBoardById(int id);

        public createBoardResponseDto CreateBoard(CreateBoardDto boardDto);
    }
}
