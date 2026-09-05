namespace Bilboard.Evaluation;

/// <summary>
/// One dataset table handed to the generator. The Python VisEval agent fills this
/// from the CSV files that <c>viseval.Dataset</c> hands to <c>Agent.generate</c>.
/// </summary>
public sealed class EvalTable
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Column headers, in order.</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>A sample of rows (values as strings, aligned with <see cref="Columns"/>).</summary>
    public List<List<string?>> Rows { get; set; } = new();

    /// <summary>Total number of rows in the source table (may exceed <see cref="Rows"/>).</summary>
    public int TotalRowCount { get; set; }
}

/// <summary>Request body of <c>POST /api/eval/generate</c>.</summary>
public sealed class GenerateRequest
{
    /// <summary>The natural-language query (VisEval <c>nl_query</c>).</summary>
    public string NlQuery { get; set; } = string.Empty;

    /// <summary>Relevant (and optionally irrelevant) tables for the query.</summary>
    public List<EvalTable> Tables { get; set; } = new();

    /// <summary>Chat model id. Defaults to the model used by the Boards page.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// When true (default) the generator is asked for exactly one component that answers
    /// the query, so it can be compared against VisEval's single-chart ground truth.
    /// Set false to get the normal multi-component dashboard.
    /// </summary>
    public bool SingleComponent { get; set; } = true;

    /// <summary>Return every agent message in the response (useful for error analysis).</summary>
    public bool IncludeTrace { get; set; }

    /// <summary>
    /// Put the table rows in the user prompt as well as in the data-analysis agent's
    /// instructions. Without this the config generator never sees the actual data — it
    /// only sees the data scientist's prose summary — and invents plausible numbers.
    /// Defaults to true; set false to measure the pipeline exactly as the chat UI runs it.
    /// </summary>
    public bool DataInPrompt { get; set; } = true;

    /// <summary>
    /// Take the reinforcing agent's final message instead of the config generator's.
    /// Defaults to false, which is exactly what Boards.razor does.
    /// </summary>
    public bool UseReinforcedOutput { get; set; }
}

public sealed class AgentMessage
{
    public string? Author { get; set; }
    public string? Text { get; set; }
}

public sealed class TokenUsage
{
    public long InputTokenCount { get; set; }
    public long OutputTokenCount { get; set; }
    public long TotalTokenCount { get; set; }
}

/// <summary>Response body of <c>POST /api/eval/generate</c>.</summary>
public sealed class GenerateResponse
{
    public bool Success { get; set; }

    /// <summary>The BIL dashboard configuration (a JSON array). This is VisEval's "code".</summary>
    public string? DashboardJson { get; set; }

    public string? ErrorMsg { get; set; }

    public List<AgentMessage> Trace { get; set; } = new();

    public TokenUsage? Usage { get; set; }

    public long ElapsedMs { get; set; }
}

/// <summary>Request body of <c>POST /api/eval/execute</c>.</summary>
public sealed class ExecuteRequest
{
    /// <summary>The BIL dashboard configuration produced by <c>/api/eval/generate</c>.</summary>
    public string DashboardJson { get; set; } = string.Empty;

    /// <summary>Include the rendered BIL HTML in the response (default true).</summary>
    public bool IncludeHtml { get; set; } = true;
}

public sealed class ChartSeries
{
    public string? Name { get; set; }

    public List<double?> Values { get; set; } = new();

    /// <summary>
    /// Optional per-series categories. Grouped scatter and grouped line charts can give
    /// each group its own x values; when empty the series uses <see cref="ChartSpec.XData"/>.
    /// </summary>
    public List<string> XData { get; set; } = new();
}

/// <summary>
/// The dashboard reduced to a single, library-neutral chart description. The Python
/// agent redraws this with matplotlib so that VisEval's SVG deconstructor can read it.
/// </summary>
public sealed class ChartSpec
{
    /// <summary>pie | bar | stacked bar | grouping bar | line | grouping line | scatter | grouping scatter | table | none</summary>
    public string Chart { get; set; } = "none";

    public string? Title { get; set; }
    public string? XName { get; set; }
    public string? YName { get; set; }

    public List<string> XData { get; set; } = new();
    public List<double?> YData { get; set; } = new();

    /// <summary>Populated for grouped/stacked charts; each series carries one value per <see cref="XData"/> entry.</summary>
    public List<ChartSeries> Series { get; set; } = new();

    /// <summary>Free-form note about how the spec was derived (which BIL component was used).</summary>
    public string? Source { get; set; }
}

/// <summary>Response body of <c>POST /api/eval/execute</c>.</summary>
public sealed class ExecuteResponse
{
    /// <summary>True when the configuration deserialized into BIL components and rendered.</summary>
    public bool Status { get; set; }

    public string? ErrorMsg { get; set; }

    /// <summary>The real BIL render (Bootstrap markup), for screenshots and manual review.</summary>
    public string? Html { get; set; }

    public int ComponentCount { get; set; }

    public List<string> ComponentTypes { get; set; } = new();

    public ChartSpec? ChartSpec { get; set; }
}
