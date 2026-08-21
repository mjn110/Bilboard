using Application.DTO.Boards;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interfaces
{
    public interface IBoardService
    {
        public IEnumerable<GetBoardDto> GetBoards();
        public GetBoardDto GetBoardById(string id);
        public BoardResponseDto CreateBoard(CreateBoardDto boardDto);
        public BoardResponseDto UpdateBoard(string id, UpdateBoardDto boardDto);
        public BoardResponseDto DeleteBoard(string id);
    }
}