using System;
using System.Collections.Generic;
using System.Text;
using Application.Common.Interface.Persistence;
using Infrastructure.Data;
using Domain.Entities;

namespace Infrastructure.Persistence
{
    public class BoardRepository : IBoardRepository
    {
        private readonly BilContext _context;
        public BoardRepository(BilContext context) 
        {
            _context = context;
        }

        public IEnumerable<Board> GetAllBoards()
        {
            return _context.Boards.ToList();
        }

        public Board GetBoardById(string boardId)
        {
            return _context.Boards.FirstOrDefault(b => b.BoardId == boardId);
        }

        public void AddBoard(Board board)
        {
            _context.Boards.Add(board);
            _context.SaveChanges();
        }

        public void UpdateBoard(Board board)
        {
            _context.Boards.Update(board);
            _context.SaveChanges();
        }

        public void RemoveBoard(Board board)
        {
            _context.Boards.Remove(board);
            _context.SaveChanges();
        }
    }
}