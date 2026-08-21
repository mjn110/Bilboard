using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Infrastructure.Model
{
    public class Value
    {
        [Key]
        public string ValueId { get; set; }
        [Required]
        public string Value { get; set; }
        public Component Component { get; set; }
        [ForeignKey("Component")]
        public string ComponentId { get; set; }
    }
}
