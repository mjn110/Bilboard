using Application.Common.Interface.Persistence;
using Application.DTO.Boards;
using Application.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;

namespace Application.Services
{
    public class BoardService : IBoardService
    {
        private readonly IBoardRepository _boardRepository;
        public BoardService(IBoardRepository boardRepository)
        { 
            _boardRepository = boardRepository;
        }

        public IEnumerable<GetBoardDto> GetBoards()
        {
            var Boards = _boardRepository.GetAllBoards().Select(b => new GetBoardDto { BoardName = b.Name });
            return Boards;
        }

        public GetBoardDto GetBoardById(string id)
        {
            var board = _boardRepository.GetBoardById(id);
            return new GetBoardDto { BoardName = board.Name };
        }

        public BoardResponseDto CreateBoard(CreateBoardDto boardDto)
        {
            _boardRepository.AddBoard(new Domain.Entities.Board { Name = boardDto.Name, DateCreated = DateTime.Now, DateModified = DateTime.Now, Access = boardDto.Access });
            
            return new BoardResponseDto
            {
                Response = "The board with name " + boardDto.Name + " has been created",
                Date = DateTime.Now
            };
        }

        public BoardResponseDto UpdateBoard(string id, UpdateBoardDto boardDto)
        {
            return new BoardResponseDto
            {
                Response = "The board with name " + boardDto.Name + " has been updated",
                Date = DateTime.Now
            };
        }

        public BoardResponseDto DeleteBoard(string id)
        {
            return new BoardResponseDto
            {
                Response = "The board with id " + id + " has been deleted",
                Date = DateTime.Now
            };
        }
    }
}