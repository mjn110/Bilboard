using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using BlazorInterfaceLibrary.Bil.Classes;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Bilboard.Evaluation;

public interface IBilExecutor
{
    Task<ExecuteResponse> ExecuteAsync(ExecuteRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// "Executes" a generated dashboard configuration: deserializes it into BIL components
/// (the step that fails in the UI when the model emits a bad config), renders it to HTML
/// through Blazor's headless <see cref="HtmlRenderer"/>, and reduces it to a single
/// library-neutral <see cref="ChartSpec"/> that the Python harness can redraw for VisEval.
/// </summary>
public sealed class BilExecutor : IBilExecutor
{
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;

    // Identical to the options used by Boards.razor.
    private static readonly JsonSerializerOptions BilOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public BilExecutor(IServiceProvider services, ILoggerFactory loggerFactory)
    {
        _services = services;
        _loggerFactory = loggerFactory;
    }

    public async Task<ExecuteResponse> ExecuteAsync(
        ExecuteRequest request,
        CancellationToken cancellationToken = default)
    {
        var response = new ExecuteResponse();

        string json = JsonPayload.ExtractJsonArray(request.DashboardJson);
        if (string.IsNullOrWhiteSpace(json))
        {
            response.ErrorMsg = "The configuration does not contain a JSON array.";
            return response;
        }

        List<BilComponent>? components;
        try
        {
            components = JsonSerializer.Deserialize<List<BilComponent>>(json, BilOptions);
        }
        catch (Exception ex)
        {
            response.ErrorMsg = $"BIL deserialization failed: {ex.Message}";
            return response;
        }

        if (components is null || components.Count == 0)
        {
            response.ErrorMsg = "The configuration produced no BIL components.";
            return response;
        }

        response.ComponentCount = components.Count;
        response.ComponentTypes = components.Select(c => c.GetType().Name).ToList();

        if (request.IncludeHtml)
        {
            try
            {
                response.Html = await RenderHtmlAsync(components);
            }
            catch (Exception ex)
            {
                response.ErrorMsg = $"BIL rendering failed: {ex.Message}";
                return response;
            }

            if (string.IsNullOrWhiteSpace(response.Html))
            {
                response.ErrorMsg = "BIL rendering produced no markup.";
                return response;
            }
        }

        try
        {
            response.ChartSpec = ChartSpecExtractor.Extract(json);
        }
        catch (Exception ex)
        {
            response.ErrorMsg = $"Could not derive a chart specification: {ex.Message}";
            return response;
        }

        response.Status = true;
        return response;
    }

    private async Task<string> RenderHtmlAsync(List<BilComponent> components)
    {
        await using var htmlRenderer = new HtmlRenderer(_services, _loggerFactory);

        return await htmlRenderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(BilBoardHost.Components)] = components
            });

            var output = await htmlRenderer.RenderComponentAsync<BilBoardHost>(parameters);
            return output.ToHtmlString();
        });
    }
}

/// <summary>
/// Reduces a BIL dashboard configuration to one chart description.
/// <para>
/// BIL's own <c>Chart</c> only carries three numeric rings, which is not enough to compare
/// against VisEval ground truth, so evaluation-mode configurations additionally carry
/// <c>ChartType</c>, <c>Labels</c>, <c>Values</c>, <c>Series</c>, <c>XName</c> and <c>YName</c>.
/// Those fields are ignored by BIL deserialization, so the same JSON still renders.
/// This extractor prefers them and falls back to the three-slice fields.
/// </para>
/// </summary>
public static class ChartSpecExtractor
{
    public static ChartSpec Extract(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return new ChartSpec { Chart = "none", Source = "not an array" };
        }

        JsonElement? chart = null;
        JsonElement? progress = null;
        JsonElement? list = null;

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string component = GetString(element, "Component") ?? string.Empty;

            if (component.Equals("Chart", StringComparison.OrdinalIgnoreCase))
            {
                chart ??= element;
            }
            else if (component.Equals("Progress", StringComparison.OrdinalIgnoreCase))
            {
                progress ??= element;
            }
            else if (component.Equals("List", StringComparison.OrdinalIgnoreCase))
            {
                list ??= element;
            }
        }

        if (chart is not null)
        {
            return FromChart(chart.Value);
        }

        if (progress is not null)
        {
            return FromProgress(progress.Value);
        }

        if (list is not null)
        {
            return new ChartSpec { Chart = "table", Source = "List component" };
        }

        return new ChartSpec { Chart = "none", Source = "no chart-like component" };
    }

    private static ChartSpec FromChart(JsonElement element)
    {
        var spec = new ChartSpec
        {
            Chart = (GetString(element, "ChartType") ?? "pie").Trim().ToLowerInvariant(),
            Title = GetString(element, "Name"),
            XName = GetString(element, "XName"),
            YName = GetString(element, "YName"),
            Source = "Chart component"
        };

        // Preferred: full arrays emitted in evaluation mode.
        if (element.TryGetProperty("Labels", out var labels) && labels.ValueKind == JsonValueKind.Array)
        {
            foreach (var label in labels.EnumerateArray())
            {
                spec.XData.Add(AsString(label));
            }
        }

        if (element.TryGetProperty("Values", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var value in values.EnumerateArray())
            {
                spec.YData.Add(AsNumber(value));
            }
        }

        // Fallback: BIL's three-slice representation.
        if (spec.XData.Count == 0)
        {
            for (int i = 1; i <= 3; i++)
            {
                if (element.TryGetProperty($"Option{i}", out var option))
                {
                    spec.XData.Add(AsString(option));
                }
            }
        }

        if (spec.YData.Count == 0)
        {
            for (int i = 1; i <= 3; i++)
            {
                if (element.TryGetProperty($"Value{i}", out var value))
                {
                    spec.YData.Add(AsNumber(value));
                }
                else if (element.TryGetProperty($"Option{i}", out var option) &&
                         option.ValueKind == JsonValueKind.Number)
                {
                    spec.YData.Add(AsNumber(option));
                }
            }
        }

        if (element.TryGetProperty("Series", out var series) && series.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in series.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var chartSeries = new ChartSeries { Name = GetString(entry, "Name") };
                if (entry.TryGetProperty("Values", out var seriesValues) &&
                    seriesValues.ValueKind == JsonValueKind.Array)
                {
                    foreach (var value in seriesValues.EnumerateArray())
                    {
                        chartSeries.Values.Add(AsNumber(value));
                    }
                }

                // Grouped scatter / line may give each group its own x values.
                if (entry.TryGetProperty("Labels", out var seriesLabels) &&
                    seriesLabels.ValueKind == JsonValueKind.Array)
                {
                    foreach (var label in seriesLabels.EnumerateArray())
                    {
                        chartSeries.XData.Add(AsString(label));
                    }
                }

                spec.Series.Add(chartSeries);
            }
        }

        return spec;
    }

    private static ChartSpec FromProgress(JsonElement element)
    {
        var spec = new ChartSpec
        {
            Chart = "bar",
            XName = GetString(element, "XName"),
            YName = GetString(element, "YName"),
            Source = "Progress component"
        };

        if (element.TryGetProperty("Bars", out var bars) && bars.ValueKind == JsonValueKind.Array)
        {
            foreach (var bar in bars.EnumerateArray())
            {
                if (bar.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                spec.XData.Add(GetString(bar, "Name") ?? string.Empty);
                spec.YData.Add(bar.TryGetProperty("Value", out var value) ? AsNumber(value) : null);
            }
        }

        return spec;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) ? AsString(property) : null;

    private static string AsString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => element.GetRawText()
    };

    private static double? AsNumber(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out var number) ? number : null;
            case JsonValueKind.String:
                return double.TryParse(
                    element.GetString(),
                    NumberStyles.Any,
                    CultureInfo.InvariantCulture,
                    out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }
}
