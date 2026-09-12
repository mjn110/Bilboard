using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTO.Boards
{
    public class ComponentDto
    {
        // Null for a component that has not been saved yet.
        public string? ComponentId { get; set; }

        public string Type { get; set; }

        public string Name { get; set; }

        public List<AttributeDto> Attributes { get; set; } = new List<AttributeDto>();
    }
}
