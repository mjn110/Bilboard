namespace Bilboard.Evaluation;

/// <summary>Input for one run of the dashboard generation pipeline.</summary>
public sealed class DashboardGenerationRequest
{
    /// <summary>The user prompt (chat message, or VisEval natural-language query).</summary>
    public string Prompt { get; set; } = string.Empty;

    /// <summary>
    /// Raw data given to the "Dashboard reinforcing agent" that runs first (the data
    /// scientist). In the UI this is the uploaded resource file; in evaluation it is a
    /// textual rendering of the dataset tables. Empty means the agent is skipped,
    /// exactly like Boards.razor.
    /// </summary>
    public string DataContext { get; set; } = string.Empty;

    /// <summary>Chat model id; null uses <see cref="DashboardPrompts.DefaultModel"/>.</summary>
    public string? Model { get; set; }

    /// <summary>Append <see cref="DashboardPrompts.EvaluationAddendum"/> to the generator instructions.</summary>
    public bool EvaluationMode { get; set; }

    /// <summary>Read the config from the reinforcing agent instead of the config generator.</summary>
    public bool UseReinforcedOutput { get; set; }

    /// <summary>
    /// Also append <see cref="DataContext"/> to the user prompt. In the chat pipeline the
    /// data only reaches the first (data-analysis) agent's instructions, so the config
    /// generator writes its JSON having never seen a row.
    /// </summary>
    public bool DataInPrompt { get; set; }
}

/// <summary>Output of one run of the dashboard generation pipeline.</summary>
public sealed class DashboardGenerationResult
{
    public bool Success { get; set; }

    /// <summary>The BIL configuration JSON array, code fences stripped.</summary>
    public string? DashboardJson { get; set; }

    public string? ErrorMsg { get; set; }

    public List<AgentMessage> Messages { get; set; } = new();

    public TokenUsage? Usage { get; set; }

    public long ElapsedMs { get; set; }
}

/// <summary>
/// The multi-agent dashboard generation pipeline. Both the Boards chat page and the
/// evaluation endpoints go through this, so the benchmark measures the shipping pipeline.
/// </summary>
public interface IDashboardGenerator
{
    Task<DashboardGenerationResult> GenerateAsync(
        DashboardGenerationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One minimal round trip to the chat model, to separate "the model is unreachable"
    /// from "the pipeline produced bad output". Cheap enough to run before a benchmark.
    /// </summary>
    Task<SelfTestResult> SelfTestAsync(string? model, CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="IDashboardGenerator.SelfTestAsync"/>.</summary>
public sealed class SelfTestResult
{
    public bool Success { get; set; }
    public string? Model { get; set; }
    public string? Reply { get; set; }
    public string? ErrorMsg { get; set; }
    public long TotalTokenCount { get; set; }
    public long ElapsedMs { get; set; }
}
