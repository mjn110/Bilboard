using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTO.Boards
{
    public class UpdateBoardDto
    {
        public string Name { get; set; }

        public DateTime DateModified { get; set; }

        public bool Access { get; set; }
    }
}
