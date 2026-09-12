using Application.DTO.Boards;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Interfaces
{
    // Every operation is scoped to the given user: another user's board is treated as not found (null).
    public interface IBoardService
    {
        public IEnumerable<GetBoardDto> GetBoards(string userId);
        public GetBoardDto? GetBoardById(string userId, string id);
        public BoardResponseDto CreateBoard(string userId, CreateBoardDto boardDto);
        public BoardResponseDto? UpdateBoard(string userId, string id, UpdateBoardDto boardDto);
        public BoardResponseDto? DeleteBoard(string userId, string id);
    }
}