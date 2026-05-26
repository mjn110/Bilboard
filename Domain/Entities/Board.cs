using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Board
    {
        public int BoardId { get; set; }
        public string BoardName { get; set; }
        public DateTime DateCreated { get; set; }
        public DateTime DateModified { get; set; }
        public ICollection<Component> Components { get; set; }
    }
}
