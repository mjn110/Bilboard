using System;
using System.Collections.Generic;
using System.Text;

namespace Application.DTO.Boards
{
    public class CreateBoardDto
    {
        public string Request { get; set; }
    }

    public class createBoardResponseDto
    {
        public string Response { get; set; }

        public DateTime Date { get; set; }
    }
}
