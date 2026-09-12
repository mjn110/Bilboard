using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTO.Boards
{
    public class CreateBoardDto
    {
        public string Name { get; set; }

        public bool Access { get; set; }

        // In display order.
        public List<ComponentDto> Components { get; set; } = new List<ComponentDto>();
    }
}
