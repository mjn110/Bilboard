using System;
using System.Collections.Generic;
using System.Text;

namespace Domain.Entities
{
    public class AttributeValue
    {
        public int AttributeValueId { get; set; }
        public string Attribute { get; set; }
        public string Value { get; set; }
        public int ComponentId { get; set; }
        public Component Component { get; set; }
    }
}
