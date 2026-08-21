using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Domain.Entities
{
    public class Board
    {
        public Board()
        {
            BoardId = Guid.NewGuid().ToString();
        }

        [Key]
        public string BoardId { get; set; }
        [Required]
        public string Name { get; set; }
        [Required]
        public DateTime DateCreated { get; set; }
        [Required]
        public DateTime DateModified { get; set; }  
        [Required]
        public bool Access { get; set; }

        public ICollection<Component> Components { get; set; }
    }
}
