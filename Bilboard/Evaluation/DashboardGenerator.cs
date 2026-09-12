using System.Diagnostics;
using System.Text;
using Bilboard.Application.Interfaces;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace Bilboard.Evaluation;

/// <summary>
/// The sequential agent workflow that turns a natural-language prompt into a BIL
/// dashboard configuration: (optional) data scientist -> config generator -> reviewer
/// -> reinforcer. Extracted verbatim from Boards.razor so the chat UI and the VisEval
/// harness exercise identical code.
/// </summary>
public sealed class DashboardGenerator : IDashboardGenerator
{
    private readonly IConsoleService _console;
    private readonly IConfiguration _configuration;

    public DashboardGenerator(IConsoleService console, IConfiguration configuration)
    {
        _console = console;
        _configuration = configuration;
    }

    public async Task<DashboardGenerationResult> GenerateAsync(
        DashboardGenerationRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = new DashboardGenerationResult();
        var stopwatch = Stopwatch.StartNew();

        string apiKey = ResolveApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            _console.WriteLineYellow("Error: OPENAI_API_KEY environment variable is not set.");
            result.ErrorMsg = "OPENAI_API_KEY is not configured on the Bilboard server.";
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            return result;
        }

        string model = string.IsNullOrWhiteSpace(request.Model)
            ? DashboardPrompts.DefaultModel
            : request.Model!;

        try
        {
            IChatClient chatClient = new ChatClient(model, apiKey).AsIChatClient();

            var agents = new List<AIAgent>();

            // Agent 0: analyse the supplied data (only when there is data to analyse).
            if (!string.IsNullOrEmpty(request.DataContext))
            {
                agents.Add(new ChatClientAgent(
                    chatClient,
                    new ChatClientAgentOptions
                    {
                        Name = DashboardPrompts.ReinforcingAgentName,
                        ChatOptions = new ChatOptions
                        {
                            Instructions = DashboardPrompts.DataScientistInstructions(request.DataContext)
                        }
                    }));
            }

            string configInstructions = request.EvaluationMode
                ? DashboardPrompts.ConfigInstructions + DashboardPrompts.EvaluationAddendum
                : DashboardPrompts.ConfigInstructions;

            // Agent 1: generate the BIL JSON configuration.
            agents.Add(new ChatClientAgent(
                chatClient,
                new ChatClientAgentOptions
                {
                    Name = DashboardPrompts.ConfigGeneratorAgentName,
                    ChatOptions = new ChatOptions { Instructions = configInstructions }
                }));

            // Agent 2: review the generated JSON.
            agents.Add(new ChatClientAgent(
                chatClient,
                new ChatClientAgentOptions
                {
                    Name = DashboardPrompts.ReviewerAgentName,
                    ChatOptions = new ChatOptions { Instructions = DashboardPrompts.ReviewInstructions }
                }));

            // Agent 3: reinforce the JSON with the reviewer's feedback.
            agents.Add(new ChatClientAgent(
                chatClient,
                new ChatClientAgentOptions
                {
                    Name = DashboardPrompts.ReinforcingAgentName,
                    ChatOptions = new ChatOptions { Instructions = DashboardPrompts.ReinforceInstructions }
                }));

            AIAgent workflow = AgentWorkflowBuilder.BuildSequential(agents).AsAIAgent();

            string userPrompt = string.IsNullOrEmpty(request.Prompt)
                ? DashboardPrompts.DefaultPrompt
                : request.Prompt;

            // The data-analysis agent carries the rows in its *instructions*, which the
            // config generator never sees. Putting them in the conversation as well is
            // what lets the generator compute real values instead of inventing them.
            if (request.DataInPrompt && !string.IsNullOrEmpty(request.DataContext))
            {
                userPrompt = userPrompt + DashboardPrompts.DataPreamble + request.DataContext;
            }

            AgentResponse response = await workflow.RunAsync(userPrompt, cancellationToken: cancellationToken);

            string targetAuthor = request.UseReinforcedOutput
                ? DashboardPrompts.ReinforcingAgentName
                : DashboardPrompts.ConfigGeneratorAgentName;

            string? selected = null;
            foreach (var message in response.Messages)
            {
                result.Messages.Add(new AgentMessage { Author = message.AuthorName, Text = message.Text });

                if (message.AuthorName == targetAuthor)
                {
                    // The reinforcing agent's name is reused by the data-scientist agent, so
                    // keep the last matching message rather than the first.
                    selected = message.Text;
                }
            }

            if (selected is null)
            {
                // Say enough to tell "the model never answered" apart from "the model
                // answered but no agent matched the name we look for".
                var authors = result.Messages
                    .Select(m => string.IsNullOrEmpty(m.Author) ? "(null)" : m.Author!)
                    .Distinct()
                    .ToList();
                bool allEmpty = result.Messages.All(m => string.IsNullOrWhiteSpace(m.Text));
                long tokens = response.Usage?.TotalTokenCount ?? 0;

                result.ErrorMsg =
                    $"No message produced by '{targetAuthor}'. " +
                    $"{result.Messages.Count} message(s) returned; authors: [{string.Join(", ", authors)}]; " +
                    $"all empty: {allEmpty}; total tokens: {tokens}." +
                    (allEmpty && tokens == 0
                        ? " Zero tokens and empty replies means the chat model was never " +
                          "successfully called - check the OpenAI key, quota and rate limits " +
                          "(GET /api/eval/selftest)."
                        : string.Empty);
            }
            else
            {
                string json = JsonPayload.ExtractJsonArray(selected);
                if (string.IsNullOrWhiteSpace(json))
                {
                    result.ErrorMsg = "The generator did not return a JSON array.";
                }
                else
                {
                    result.Success = true;
                    result.DashboardJson = json;
                }
            }

            var usage = response.Usage;
            if (usage is not null)
            {
                result.Usage = new TokenUsage
                {
                    InputTokenCount = usage.InputTokenCount ?? 0,
                    OutputTokenCount = usage.OutputTokenCount ?? 0,
                    TotalTokenCount = usage.TotalTokenCount ?? 0
                };
            }
        }
        catch (Exception ex)
        {
            _console.WriteLineCyan($"Error: {ex.Message}");
            result.Success = false;
            result.ErrorMsg = ex.Message;
        }

        result.ElapsedMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    public async Task<SelfTestResult> SelfTestAsync(
        string? model,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new SelfTestResult
        {
            Model = string.IsNullOrWhiteSpace(model) ? DashboardPrompts.DefaultModel : model
        };

        string apiKey = ResolveApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            result.ErrorMsg = "OPENAI_API_KEY is not configured on the Bilboard server.";
            result.ElapsedMs = stopwatch.ElapsedMilliseconds;
            return result;
        }

        try
        {
            IChatClient chatClient = new ChatClient(result.Model, apiKey).AsIChatClient();
            var response = await chatClient.GetResponseAsync(
                "Reply with the single word: ok",
                cancellationToken: cancellationToken);

            result.Reply = response.Text;
            result.TotalTokenCount = response.Usage?.TotalTokenCount ?? 0;
            result.Success = !string.IsNullOrWhiteSpace(response.Text);

            if (!result.Success)
            {
                result.ErrorMsg = "The model returned an empty reply.";
            }
        }
        catch (Exception ex)
        {
            // This is where an invalid key, exhausted quota or rate limit shows up.
            result.ErrorMsg = $"{ex.GetType().Name}: {ex.Message}";
        }

        result.ElapsedMs = stopwatch.ElapsedMilliseconds;
        return result;
    }

    private string ResolveApiKey()
    {
        string? key = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        if (!string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        key = _configuration["OpenAI:ApiKey"];
        // appsettings.json ships a "${OPENAI_API_KEY}" placeholder; ignore it when unexpanded.
        if (!string.IsNullOrWhiteSpace(key) && !key.StartsWith("${", StringComparison.Ordinal))
        {
            return key;
        }

        return string.Empty;
    }
}

/// <summary>Helpers for pulling a JSON array out of a chat completion.</summary>
public static class JsonPayload
{
    /// <summary>
    /// Strips markdown code fences and any prose around the configuration, returning the
    /// outermost JSON array. Returns an empty string when no array can be found.
    /// </summary>
    public static string ExtractJsonArray(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        string trimmed = text.Trim();

        // ```json ... ```  /  ``` ... ```
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewline = trimmed.IndexOf('\n');
            int closingFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline > 0 && closingFence > firstNewline)
            {
                trimmed = trimmed.Substring(firstNewline + 1, closingFence - firstNewline - 1).Trim();
            }
        }

        int start = trimmed.IndexOf('[');
        if (start < 0)
        {
            return string.Empty;
        }

        int depth = 0;
        bool inString = false;
        bool escaped = false;
        var buffer = new StringBuilder();

        for (int i = start; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            buffer.Append(c);

            if (escaped)
            {
                escaped = false;
                continue;
            }

            if (c == '\\' && inString)
            {
                escaped = true;
                continue;
            }

            if (c == '"')
            {
                inString = !inString;
                continue;
            }

            if (inString)
            {
                continue;
            }

            if (c == '[')
            {
                depth++;
            }
            else if (c == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return buffer.ToString();
                }
            }
        }

        return string.Empty;
    }
}
