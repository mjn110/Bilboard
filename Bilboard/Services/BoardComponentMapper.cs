using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Application.DTO.Boards;
using BlazorInterfaceLibrary.Bil.Classes;

namespace Bilboard.Services
{
    /// <summary>
    /// Converts between BIL dashboard configurations and the stored Component/Attribute shape.
    /// <para>
    /// A BIL configuration is a flat JSON array: each object is one component, with its type in
    /// "Component" and every setting and piece of data as a top-level property. A component is
    /// stored as that type plus one attribute per remaining property, whose value is kept as JSON
    /// text so strings, numbers, booleans and arrays come back with their original types.
    /// Loading rebuilds the JSON and hands it to BIL's own deserializer, so a saved board goes
    /// through exactly the path a freshly generated one does.
    /// </para>
    /// </summary>
    public static class BoardComponentMapper
    {
        private const string TypeProperty = "Component";
        private const string NameProperty = "Name";

        // The options the Boards page has always used to deserialize BIL configurations.
        public static readonly JsonSerializerOptions BilOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        // Keeps stored values readable (no \u escapes for &, <, £ ...); they never end up in HTML.
        private static readonly JsonSerializerOptions StoreOptions = new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>Splits a BIL configuration (a JSON array) into components ready to be saved.</summary>
        public static List<ComponentDto> FromDashboardJson(string dashboardJson)
        {
            var components = new List<ComponentDto>();

            foreach (var node in JsonNode.Parse(dashboardJson)!.AsArray())
            {
                var item = node!.AsObject();
                string type = item[TypeProperty]!.GetValue<string>();

                components.Add(new ComponentDto
                {
                    Type = type,
                    // A readable label for the component; Badges and Charts carry one in "Name".
                    Name = item[NameProperty] is JsonValue name && name.TryGetValue(out string? label) && !string.IsNullOrWhiteSpace(label)
                        ? label
                        : type,
                    Attributes = item
                        .Where(property => property.Key != TypeProperty)
                        .Select(property => new AttributeDto
                        {
                            Name = property.Key,
                            Value = property.Value?.ToJsonString(StoreOptions) ?? "null"
                        })
                        .ToList()
                });
            }

            return components;
        }

        /// <summary>
        /// Rebuilds a stored component's BIL configuration and deserializes it into the
        /// <see cref="BilComponent"/> the renderer expects. Throws when the stored data does not
        /// make a valid BIL component.
        /// </summary>
        public static BilComponent ToBilComponent(ComponentDto component)
        {
            // BIL only recognises the type discriminator when it is the first property.
            var item = new JsonObject { [TypeProperty] = component.Type };
            foreach (var attribute in component.Attributes)
            {
                item[attribute.Name] = JsonNode.Parse(attribute.Value);
            }

            return item.Deserialize<BilComponent>(BilOptions)
                ?? throw new JsonException($"Component '{component.Name}' could not be rebuilt.");
        }
    }
}
