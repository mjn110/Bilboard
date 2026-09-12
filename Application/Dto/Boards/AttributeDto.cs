using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTO.Boards
{
    public class AttributeDto
    {
        public string Name { get; set; }

        // JSON text of the value, e.g. "warning", 40, true or [ ... ].
        public string Value { get; set; }
    }
}
