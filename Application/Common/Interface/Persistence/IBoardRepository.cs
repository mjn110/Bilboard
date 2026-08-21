using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Common.Interface.Persistence
{
    public interface IBoardRepository
    {
        IEnumerable<Board> GetAllBoards();
        Board GetBoardById(string boardId);
        void AddBoard(Board board);
        void UpdateBoard(Board board);
        void RemoveBoard(Board board);
    }
}