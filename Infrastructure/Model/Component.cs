using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Infrastructure.Model
{
    public class Component
    {
        [Key]
        public string ComponentId { get; set; }
        [Required]
        public string Type { get; set; }
        [Required]
        public string Name { get; set; }
        public Board Board { get; set; }
        [ForeignKey("Board")]
        public string BoardId { get; set; }
    }
}
