using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTO.Boards
{
    public class UpdateBoardDto
    {
        public string Name { get; set; }

        public bool Access { get; set; }

        // The board's complete component list, in display order. Components sent with their
        // ComponentId are updated, those without one are added, and saved components that are
        // missing from the list are removed.
        public List<ComponentDto> Components { get; set; } = new List<ComponentDto>();
    }
}
