using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text;

namespace Domain.Entities
{
    public class Component
    {
        public Component()
        {
            ComponentId = Guid.NewGuid().ToString();
        }

        [Key]
        public string ComponentId { get; set; }
        // BIL component type: Badge, Chart, Progress, List or Slider.
        [Required]
        public string Type { get; set; }
        // Readable label: the component's "Name" setting when it has one, otherwise its type.
        [Required]
        public string Name { get; set; }
        // Place on the board; the BIL grid renders components in this order.
        [Required]
        public int Position { get; set; }
        public Board Board { get; set; }
        [ForeignKey("Board")]
        public string BoardId { get; set; }

        public ICollection<Attribute> Attributes { get; set; } = new List<Attribute>();
    }
}
