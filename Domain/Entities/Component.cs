using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class Component
    {
        public int ComponentId { get; set; }
        public string ComponentType { get; set; }
        public int BoardId { get; set; }
        public Board Board { get; set; }
        public ICollection<AttributeValue> AttributeValues { get; set; }
    }
}
