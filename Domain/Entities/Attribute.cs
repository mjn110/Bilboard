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
        // Name of the BIL setting, e.g. Color, WidthLarge or Value1.
        [Required]
        public string Name { get; set; }
        // The setting's value as JSON text ("warning", 40, true, [ ... ]) so it keeps its type.
        [Required]
        public string Value { get; set; }
        public Component Component { get; set; }
        [ForeignKey("Component")]
        public string ComponentId { get; set; }
    }
}
