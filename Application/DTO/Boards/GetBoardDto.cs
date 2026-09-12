using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTO.Boards
{
    public class GetBoardDto
    {
        public string BoardId { get; set; }

        public string BoardName { get; set; }

        public bool Access { get; set; }

        // Filled only when a single board is requested, in display order.
        public List<ComponentDto> Components { get; set; } = new List<ComponentDto>();
    }
}
