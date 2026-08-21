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
        [Required]
        public string Type { get; set; }
        [Required]
        public string Name { get; set; }
        public Board Board { get; set; }
        [ForeignKey("Board")]
        public string BoardId { get; set; }

        public ICollection<Attribute> Attributes { get; set; }
    }
}
