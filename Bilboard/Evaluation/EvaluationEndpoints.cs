using System.Text;

namespace Bilboard.Evaluation;

/// <summary>
/// HTTP surface used by the Python VisEval harness. Two endpoints mirror the two halves
/// of <c>Boards.razor.GenerateDashboard()</c>:
/// <list type="bullet">
///   <item><c>POST /api/eval/generate</c> — run the agent workflow, return the BIL config.</item>
///   <item><c>POST /api/eval/execute</c>  — deserialize and render that config through BIL.</item>
/// </list>
/// The group is disabled unless <c>Evaluation:Enabled</c> is true, and (when
/// <c>Evaluation:ApiKey</c> is set) every request must carry a matching <c>X-Eval-Key</c> header.
/// </summary>
public static class EvaluationEndpoints
{
    public const string ApiKeyHeader = "X-Eval-Key";

    public static IServiceCollection AddEvaluation(this IServiceCollection services)
    {
        services.AddScoped<IDashboardGenerator, DashboardGenerator>();
        services.AddScoped<IBilExecutor, BilExecutor>();
        return services;
    }

    public static IEndpointRouteBuilder MapEvaluationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
        var environment = endpoints.ServiceProvider.GetRequiredService<IWebHostEnvironment>();

        bool enabled = configuration.GetValue("Evaluation:Enabled", environment.IsDevelopment());
        if (!enabled)
        {
            return endpoints;
        }

        string? apiKey = configuration["Evaluation:ApiKey"];

        var group = endpoints.MapGroup("/api/eval").DisableAntiforgery();

        group.AddEndpointFilter(async (EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
        {
            if (!string.IsNullOrEmpty(apiKey))
            {
                var provided = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
                if (!string.Equals(provided, apiKey, StringComparison.Ordinal))
                {
                    return (object?)Results.Json(
                        new { error = $"Missing or invalid {ApiKeyHeader} header." },
                        statusCode: StatusCodes.Status401Unauthorized);
                }
            }

            return await next(context);
        });

        group.MapGet("/health", () => Results.Ok(new
        {
            status = "ok",
            service = "bilboard-evaluation",
            model = DashboardPrompts.DefaultModel,
            openAiConfigured = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OPENAI_API_KEY"))
                               || !string.IsNullOrEmpty(configuration["OpenAI:ApiKey"])
        }));

        // ---------------------------------------------------------------- generate
        group.MapPost("/generate", async (
            GenerateRequest request,
            IDashboardGenerator generator,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.NlQuery))
            {
                return Results.BadRequest(new GenerateResponse
                {
                    Success = false,
                    ErrorMsg = "nlQuery is required."
                });
            }

            var generation = await generator.GenerateAsync(
                new DashboardGenerationRequest
                {
                    Prompt = request.NlQuery,
                    DataContext = DescribeTables(request.Tables),
                    Model = request.Model,
                    EvaluationMode = request.SingleComponent,
                    UseReinforcedOutput = request.UseReinforcedOutput,
                    DataInPrompt = request.DataInPrompt
                },
                cancellationToken);

            return Results.Ok(new GenerateResponse
            {
                Success = generation.Success,
                DashboardJson = generation.DashboardJson,
                ErrorMsg = generation.ErrorMsg,
                Usage = generation.Usage,
                ElapsedMs = generation.ElapsedMs,
                Trace = request.IncludeTrace ? generation.Messages : new List<AgentMessage>()
            });
        });

        // ----------------------------------------------------------------- execute
        group.MapPost("/execute", async (
            ExecuteRequest request,
            IBilExecutor executor,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.DashboardJson))
            {
                return Results.BadRequest(new ExecuteResponse
                {
                    Status = false,
                    ErrorMsg = "dashboardJson is required."
                });
            }

            var result = await executor.ExecuteAsync(request, cancellationToken);
            return Results.Ok(result);
        });

        return endpoints;
    }

    /// <summary>
    /// Renders the dataset tables as text for the data-analysis agent. This plays the same
    /// role the uploaded resource file plays on the Boards page, where the raw file content
    /// is handed to the first agent.
    /// </summary>
    internal static string DescribeTables(List<EvalTable>? tables)
    {
        if (tables is null || tables.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        foreach (var table in tables)
        {
            builder.Append("Table: ").AppendLine(table.Name);
            builder.Append("Columns: ").AppendLine(string.Join(", ", table.Columns));

            if (table.TotalRowCount > table.Rows.Count)
            {
                builder.AppendLine(
                    $"Rows shown: {table.Rows.Count} of {table.TotalRowCount} (sample).");
            }
            else
            {
                builder.AppendLine($"Rows: {table.Rows.Count}.");
            }

            builder.AppendLine("CSV:");
            builder.AppendLine(string.Join(",", table.Columns.Select(Escape)));
            foreach (var row in table.Rows)
            {
                builder.AppendLine(string.Join(",", row.Select(cell => Escape(cell ?? string.Empty))));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        return value;
    }
}
