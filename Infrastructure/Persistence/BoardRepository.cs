using System;
using System.Collections.Generic;
using System.Text;
using Application.Common.Interface.Persistence;
using Infrastructure.Data;
using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence
{
    public class BoardRepository : IBoardRepository
    {
        private readonly BilContext _context;
        public BoardRepository(BilContext context)
        {
            _context = context;
        }

        public IEnumerable<Board> GetBoardsByUserId(string userId)
        {
            return _context.Boards
                .Where(b => b.UserId == userId)
                .OrderByDescending(b => b.DateModified)
                .ToList();
        }

        // Filtering on the owner here means another user's board is simply not found.
        public Board? GetBoardById(string userId, string boardId)
        {
            return _context.Boards
                .Include(b => b.Components)
                    .ThenInclude(c => c.Attributes)
                .FirstOrDefault(b => b.BoardId == boardId && b.UserId == userId);
        }

        public void AddBoard(Board board)
        {
            _context.Boards.Add(board);
            _context.SaveChanges();
        }

        public void UpdateBoard(Board board)
        {
            // A board loaded by GetBoardById is tracked, so SaveChanges already sees its edited,
            // added and removed components and attributes. Update() is only for a detached board:
            // on a tracked graph it would mark the new children as Modified (their keys are set in
            // their constructors) and the save would fail.
            if (_context.Entry(board).State == EntityState.Detached)
            {
                _context.Boards.Update(board);
            }
            _context.SaveChanges();
        }

        public void RemoveBoard(Board board)
        {
            _context.Boards.Remove(board);
            _context.SaveChanges();
        }
    }
}