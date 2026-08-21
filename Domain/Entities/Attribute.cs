using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Domain.Entities
{
    public class Attribute
    {
        public Attribute()
        {
            AttributeId = Guid.NewGuid().ToString();
        }

        [Key]
        public string AttributeId { get; set; }
        [Required]
        public string Value { get; set; }
        public Component Component { get; set; }
        [ForeignKey("Component")]
        public string ComponentId { get; set; }
    }
}
