using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace Infrastructure.Model
{
    public class Board
    {
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
    }
}
