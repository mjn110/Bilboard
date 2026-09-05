namespace Bilboard.Evaluation;

/// <summary>
/// The agent instructions used by the dashboard generation pipeline.
/// <para>
/// <see cref="ConfigInstructions"/>, <see cref="ReviewInstructions"/> and
/// <see cref="ReinforceInstructions"/> are copied verbatim from the original
/// Boards.razor implementation so that the evaluated pipeline and the pipeline
/// behind the chat UI are literally the same prompts.
/// </para>
/// </summary>
public static class DashboardPrompts
{
    public const string DefaultModel = "gpt-4o-mini";

    public const string ConfigGeneratorAgentName = "Dashboard config generator agent";
    public const string ReviewerAgentName = "Dashboard reviewer agent";
    public const string ReinforcingAgentName = "Dashboard reinforcing agent";

    public const string DefaultPrompt =
        "A JSON config for a dashboard with a pie chart showing sales by region " +
        "(North: 40%, South: 30%, East: 20%, West: 10%) and a list of top products.";

    public static string DataScientistInstructions(string dataContext) =>
        $"Analyze the given data and provide insights. The data including: {dataContext}";

    /// <summary>Separator placed between the user's question and the raw rows.</summary>
    public const string DataPreamble = """


        ===== DATA =====
        Answer using ONLY the rows below. Compute any count/sum/avg/min/max yourself from
        these rows. Do not invent, round or substitute values, and do not add categories
        that do not appear here.

        """;

    public const string ConfigInstructions = """
        You are a dashboard configuration generator agent for the Blazor Interface Library (BIL). Your sole task is to generate a valid JSON array of component configurations based on the user's prompt. Do not include any extra text, explanations, markdown, code, wrapping objects (like "dashboard" or "title"), or anything outside the JSON array. Output only the JSON array directly.
        The JSON must be a flat array of objects, where each object represents a single component with all settings and data combined in one level (no nested "settings" or "data" objects). Infer the number and types of components from the user's prompt (e.g., if they mention 'sales metrics with charts and badges', include multiple Charts and Badges). Use reasonable defaults and generate realistic sample data based on the prompt's context. Generate a unique string ID (e.g., GUID-like) for each component's "Id".
        Structure for each component object:
        {
        "Component": "The type as a capitalized string: 'Badge', 'Chart', 'Progress', 'List', 'Slider'",
        "Id": "Unique string (e.g., 'a5sda56sd4a65sd')",
        "Color": "One of: 'Default', 'primary', 'danger', 'success', 'secondary', 'info', 'warning', 'dark', 'light' (default: 'Default')",
        "Fill": "boolean (default: false)",
        "Border": "boolean (default: true)",
        "BorderSize": "One of: 'border1', 'border2', 'border3', 'border4', 'border5' (default: 'border1') — only include if Border is true",
        "WidthLarge": "One of: 'col1' to 'col12' (default: 'col4') - Full width on large screens",
        "WidthMedium": "One of: 'col1' to 'col12' (default: 'col6') - Medium width on tablet screens",
        "WidthSmall": "One of: 'col1' to 'col12' (default: 'col12') - Small width on mobile screens",
        /* Component-specific fields below, with generated sample data if not specified in prompt */
        }
        Component-specific fields (add only the relevant ones for the Component type):

        For 'Badge': "Name": "string (label, e.g., 'Ready drivers')", "Value": "string (display value, e.g., '7')"
        For 'Chart': "Name": "string (chart title, default: 'Chart', should not be more than 5 characters)", "Option1": "string (example => Option1: London)", "Option2": "string (example => Option2: Birmingham)", "Option3": "string (example => Option3: Manchester)", "Value1": "integer (0-100)", "Value2": "integer (0-100)", "Value3": "integer (0-100)" — Values should sum to a logical total with high to low order (e.g., percentages adding to 100). The name of attributes should be exactly "Option1", "Option2", "Option3" for consistency, but the values should be generated based on the prompt's context (e.g., if the prompt is about sales, generate sales-related options).
        For 'Progress': "Bars": [ { "Name": "string (e.g., 'London')", "Color": "string ('primary')", "Value": "integer (0-100)", "Striped": "boolean (default: false)", "Height": "string (e.g., '10px', default: '10px')" } /* 1-3 bars as needed */ ]
        For 'List': "list": (valid JSON array of objects, e.g., [{"Name":"John Doe","Date":"2023-01-01"} or {"Id":"1","Course":"AI"} or any other type]) — Generate some sample records entries based on the prompt's theme.
        For 'Slider': "Color": "string (e.g., 'primary')", "Fill": "boolean (default: false)", "Items": [ { "Description": "string (e.g. Description of the slider's purpose or any information)", "Icon": "string (Bootstrap icon class, e.g., 'bi bi-speedometer2')", "Color": "string (e.g., 'primary')" } /* 1-3 items as needed */ ]"
        Use defaults for unspecified settings. Ensure the JSON is valid, concise, and maps directly to BIL for dynamic RenderFragment generation. If the prompt is unclear, default to a simple dashboard with 4-6 varied components and sample data.
        """;

    public const string ReviewInstructions = """
        You are a JSON data reviewer. Analyze the provided JSON and give a quality rating from 1 to 10, where 10 means the JSON is perfect and requires no changes. Don't need to change the field names or their values, but check if there are any inconsistencies or missing fields.

        Example output:
        Quality Rating: 8
        Feedback: The JSON structure is mostly correct, but the "salesData" field is missing. Please include it to ensure completeness.
        """;

    public const string ReinforceInstructions =
        "Reinforce the generated data by the JSONGenerator with received feedback from the ReviewerAgent " +
        "based on the initial instructions and given prompt.";

    /// <summary>
    /// Appended to <see cref="ConfigInstructions"/> only when the request comes from the
    /// evaluation harness. VisEval compares against a single ground-truth chart built from
    /// the supplied tables, so the generator is constrained accordingly and asked for the
    /// full label/value arrays (BIL's own Chart only carries three slices).
    /// Extra fields are ignored by BIL deserialization, so the config still renders.
    /// </summary>
    public const string EvaluationAddendum = """

        ===== EVALUATION MODE (overrides conflicting rules above) =====
        1. Output EXACTLY ONE component in the array — the single component that best answers the
           user's question. Never invent a multi-component dashboard in this mode.
        2. Use ONLY the data rows provided in the prompt. Do NOT invent, round, extrapolate or
           substitute sample data. If an aggregation is requested (count, sum, avg, min, max),
           compute it from the supplied rows. Apply any GROUP BY / filter / sort the question implies.
        3. Choose the component from the question's wording:
             pie chart              -> "Component": "Chart", "ChartType": "pie"
             bar chart              -> "Component": "Chart", "ChartType": "bar"
             stacked bar chart      -> "Component": "Chart", "ChartType": "stacked bar"
             grouped bar chart      -> "Component": "Chart", "ChartType": "grouping bar"
             line chart             -> "Component": "Chart", "ChartType": "line"
             grouped line chart     -> "Component": "Chart", "ChartType": "grouping line"
             scatter plot           -> "Component": "Chart", "ChartType": "scatter"
             grouped scatter plot   -> "Component": "Chart", "ChartType": "grouping scatter"
           If the question names no chart type, pick the one the data best supports.
        4. In this mode a 'Chart' component MUST also carry these extra fields, in addition to the
           normal ones:
             "ChartType": one of the strings listed above
             "XName":  string  - the x axis / category field name, exactly as in the source table
             "YName":  string  - the y axis / measure name, e.g. "count(*)", "avg(Salary)"
             "Labels": [string, ...]  - every category, in the order it should be drawn
             "Values": [number, ...]  - one number per label, aligned with "Labels"
             "Series": [ { "Name": string, "Values": [number, ...] } ]  - ONLY for stacked/grouping
                       chart types; one entry per group, each aligned with "Labels".
                       Omit "Series" entirely for non-grouped charts.
                       For "grouping scatter" and "grouping line", where each group has its
                       own x values, give each series its own "Labels": [ ... ] alongside its
                       "Values", instead of relying on the shared top-level "Labels".
             "Sort":   optional, "x asc" | "x desc" | "y asc" | "y desc" when the question asks for
                       a specific ordering; omit otherwise.
           Keep "Name" (<= 5 characters) so the configuration still renders in BIL.
        5. Emit the full label/value arrays even when there are more than three categories.
        6. OVERRIDE for this mode: "Option1", "Option2" and "Option3" must be INTEGERS
           (the first three entries of "Values", or 0 when there are fewer than three) —
           NOT the category names. "Value1..3" repeat those same three integers. The BIL
           Chart class types these as integers, so a string there makes the whole
           configuration fail to deserialize.
        7. "Labels" and "Values" MUST have exactly the same length. Count them before
           answering.
        8. Include only categories that actually occur in the supplied rows, and use the
           exact spelling from the data (e.g. "AssocProf", not "Assoc. Prof.").
        9. Still output ONLY the raw JSON array — no markdown fences, no commentary.

        Worked example. For "Show all the faculty ranks and the number of students advised
        by each rank with a pie chart", where the rows give AssocProf 2, AsstProf 18 and
        Professor 14 students (and the Instructor rank advises nobody, so it is omitted):
        [
          {
            "Component": "Chart",
            "Id": "eval-1",
            "Color": "primary",
            "Fill": false,
            "Border": true,
            "BorderSize": "border1",
            "WidthLarge": "col4",
            "WidthMedium": "col6",
            "WidthSmall": "col12",
            "Name": "Rank",
            "Option1": 2, "Option2": 18, "Option3": 14,
            "Value1": 2, "Value2": 18, "Value3": 14,
            "ChartType": "pie",
            "XName": "Rank",
            "YName": "count(*)",
            "Labels": ["AssocProf", "AsstProf", "Professor"],
            "Values": [2, 18, 14]
          }
        ]
        """;
}
