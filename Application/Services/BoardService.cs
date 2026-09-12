using Application.Common.Interface.Persistence;
using Application.DTO.Boards;
using Application.Interfaces;
using Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;
using Attribute = Domain.Entities.Attribute;

namespace Application.Services
{
    public class BoardService : IBoardService
    {
        private readonly IBoardRepository _boardRepository;
        public BoardService(IBoardRepository boardRepository)
        {
            _boardRepository = boardRepository;
        }

        public IEnumerable<GetBoardDto> GetBoards(string userId)
        {
            var Boards = _boardRepository.GetBoardsByUserId(userId).Select(b => new GetBoardDto { BoardId = b.BoardId, BoardName = b.Name, Access = b.Access });
            return Boards;
        }

        public GetBoardDto? GetBoardById(string userId, string id)
        {
            var board = _boardRepository.GetBoardById(userId, id);
            if (board == null)
            {
                return null;
            }

            return new GetBoardDto
            {
                BoardId = board.BoardId,
                BoardName = board.Name,
                Access = board.Access,
                Components = board.Components
                    .OrderBy(c => c.Position)
                    .Select(c => new ComponentDto
                    {
                        ComponentId = c.ComponentId,
                        Type = c.Type,
                        Name = c.Name,
                        Attributes = c.Attributes.Select(a => new AttributeDto { Name = a.Name, Value = a.Value }).ToList()
                    })
                    .ToList()
            };
        }

        public BoardResponseDto CreateBoard(string userId, CreateBoardDto boardDto)
        {
            // Npgsql only writes UTC values to "timestamp with time zone" columns.
            var now = DateTime.UtcNow;
            var board = new Board { Name = boardDto.Name, DateCreated = now, DateModified = now, Access = boardDto.Access, UserId = userId };
            SyncComponents(board, boardDto.Components);
            _boardRepository.AddBoard(board);

            return new BoardResponseDto
            {
                BoardId = board.BoardId,
                Response = "The board with name " + boardDto.Name + " has been created",
                Date = now
            };
        }

        public BoardResponseDto? UpdateBoard(string userId, string id, UpdateBoardDto boardDto)
        {
            var board = _boardRepository.GetBoardById(userId, id);
            if (board == null)
            {
                return null;
            }

            board.Name = boardDto.Name;
            board.Access = boardDto.Access;
            board.DateModified = DateTime.UtcNow;
            SyncComponents(board, boardDto.Components);
            _boardRepository.UpdateBoard(board);

            return new BoardResponseDto
            {
                BoardId = board.BoardId,
                Response = "The board with name " + boardDto.Name + " has been updated",
                Date = board.DateModified
            };
        }

        public BoardResponseDto? DeleteBoard(string userId, string id)
        {
            var board = _boardRepository.GetBoardById(userId, id);
            if (board == null)
            {
                return null;
            }

            _boardRepository.RemoveBoard(board);

            return new BoardResponseDto
            {
                BoardId = board.BoardId,
                Response = "The board with id " + id + " has been deleted",
                Date = DateTime.UtcNow
            };
        }

        // Makes the board's components match the given list, which is in display order.
        private static void SyncComponents(Board board, List<ComponentDto> components)
        {
            // Only this board's own components can be matched, so an id sent by the client can
            // never reach another board's records. Unknown or repeated ids are added as new.
            var existing = board.Components.ToDictionary(c => c.ComponentId);

            for (var position = 0; position < components.Count; position++)
            {
                var dto = components[position];
                if (dto.ComponentId == null || !existing.Remove(dto.ComponentId, out var component))
                {
                    component = new Component();
                    board.Components.Add(component);
                }

                component.Type = dto.Type;
                component.Name = dto.Name;
                component.Position = position;
                SyncAttributes(component, dto.Attributes);
            }

            // Components that were not sent back have been removed from the board; their
            // attributes are deleted with them.
            foreach (var removed in existing.Values)
            {
                board.Components.Remove(removed);
            }
        }

        // Attributes are matched by name, so a setting that is still there keeps its record.
        private static void SyncAttributes(Component component, List<AttributeDto> attributes)
        {
            var existing = component.Attributes.ToDictionary(a => a.Name);

            // A name sent twice keeps its last value instead of creating a second record.
            foreach (var dto in attributes.GroupBy(a => a.Name).Select(g => g.Last()))
            {
                if (!existing.Remove(dto.Name, out var attribute))
                {
                    attribute = new Attribute { Name = dto.Name };
                    component.Attributes.Add(attribute);
                }

                attribute.Value = dto.Value;
            }

            foreach (var removed in existing.Values)
            {
                component.Attributes.Remove(removed);
            }
        }
    }
}