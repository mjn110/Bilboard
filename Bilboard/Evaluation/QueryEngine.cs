using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bilboard.Evaluation;

/// <summary>
/// Reads a JSON string, number or boolean as a string. Models write
/// <c>"value": 2016</c> as often as <c>"value": "2016"</c>, and a type error there
/// throws the whole query away.
/// </summary>
internal sealed class LooseStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                return reader.GetString();
            case JsonTokenType.Number:
                return reader.TryGetInt64(out long whole)
                    ? whole.ToString(CultureInfo.InvariantCulture)
                    : reader.GetDouble().ToString(CultureInfo.InvariantCulture);
            case JsonTokenType.True:
                return "true";
            case JsonTokenType.False:
                return "false";
            case JsonTokenType.Null:
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteStringValue(value);
        }
    }
}

/// <summary>
/// The computation a chart is built from. The generator emits this instead of typing
/// the numbers itself, and <see cref="QueryEngine"/> evaluates it against the real rows.
/// </summary>
public sealed class ChartQuery
{
    /// <summary>Source table name.</summary>
    public string? From { get; set; }

    /// <summary>Optional inner join.</summary>
    public QueryJoin? Join { get; set; }

    /// <summary>
    /// Optional chain of inner joins, applied in order after <see cref="Join"/>. Needed
    /// whenever the answer spans more than two tables (A -&gt; link table -&gt; B).
    /// </summary>
    public List<QueryJoin> Joins { get; set; } = new();

    /// <summary>Optional row filters, ANDed together.</summary>
    public List<QueryFilter> Where { get; set; } = new();

    /// <summary>Field whose distinct values become the categories (x axis / slices).</summary>
    public string? GroupBy { get; set; }

    /// <summary>Optional second grouping field for grouped and stacked charts.</summary>
    public string? SeriesBy { get; set; }

    /// <summary>How each group is reduced to a number. Omit for raw x/y (scatter).</summary>
    public QueryAggregate? Aggregate { get; set; }

    /// <summary>Raw mode (no aggregate): the x and y columns to plot directly.</summary>
    public string? XField { get; set; }

    public string? YField { get; set; }

    /// <summary>"x asc" | "x desc" | "y asc" | "y desc".</summary>
    public string? Sort { get; set; }

    /// <summary>Keep only the first N categories after sorting.</summary>
    public int? Limit { get; set; }

    public bool IsUsable() =>
        !string.IsNullOrWhiteSpace(GroupBy)
        || (!string.IsNullOrWhiteSpace(XField) && !string.IsNullOrWhiteSpace(YField));
}

public sealed class QueryJoin
{
    public string? Table { get; set; }

    /// <summary>[left field, right field].</summary>
    public List<string> On { get; set; } = new();
}

public sealed class QueryFilter
{
    public string? Field { get; set; }

    /// <summary>= != &gt; &gt;= &lt; &lt;= contains</summary>
    public string? Op { get; set; }

    [JsonConverter(typeof(LooseStringConverter))]
    public string? Value { get; set; }
}

public sealed class QueryAggregate
{
    /// <summary>count | count_distinct | sum | avg | min | max | none</summary>
    public string? Fn { get; set; }

    public string? Field { get; set; }
}

public sealed class QueryResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public List<string> Labels { get; set; } = new();
    public List<double?> Values { get; set; } = new();
    public List<ChartSeries> Series { get; set; } = new();
}

/// <summary>
/// A small group-by engine over the tables supplied with the request. This is what makes
/// the numbers on the chart exact: the model decides <em>what</em> to compute, this
/// decides <em>the answer</em>.
/// </summary>
public static class QueryEngine
{
    private const int MaxJoinRows = 2_000_000;

    public static QueryResult Run(ChartQuery? query, List<EvalTable>? tables)
    {
        var result = new QueryResult();

        if (query is null || !query.IsUsable())
        {
            result.Error = "No usable query (needs groupBy, or xField and yField).";
            return result;
        }

        if (tables is null || tables.Count == 0)
        {
            result.Error = "No tables supplied to execute the query against.";
            return result;
        }

        EvalTable? left = FindTable(tables, query.From) ?? (tables.Count == 1 ? tables[0] : null);
        if (left is null)
        {
            result.Error = $"Unknown table '{query.From}'. Available: {string.Join(", ", tables.Select(t => t.Name))}.";
            return result;
        }

        // Never aggregate a truncated sample: a plausible wrong number is worse than an error.
        string? truncated = CheckComplete(left);
        if (truncated is not null)
        {
            result.Error = truncated;
            return result;
        }

        List<Dictionary<string, string?>> rows = Materialise(left);

        var joins = new List<QueryJoin>();
        if (query.Join is not null && !string.IsNullOrWhiteSpace(query.Join.Table))
        {
            joins.Add(query.Join);
        }

        joins.AddRange(query.Joins.Where(j => !string.IsNullOrWhiteSpace(j.Table)));

        foreach (var join in joins)
        {
            EvalTable? right = FindTable(tables, join.Table);
            if (right is null)
            {
                result.Error = $"Unknown join table '{join.Table}'.";
                return result;
            }

            truncated = CheckComplete(right);
            if (truncated is not null)
            {
                result.Error = truncated;
                return result;
            }

            if (join.On.Count < 2)
            {
                result.Error = "join.on must name the left and right field.";
                return result;
            }

            rows = InnerJoin(rows, Materialise(right), join.On[0], join.On[1], out string? joinError);
            if (joinError is not null)
            {
                result.Error = joinError;
                return result;
            }
        }

        foreach (var filter in query.Where)
        {
            rows = rows.Where(row => Matches(row, filter)).ToList();
        }

        if (rows.Count == 0)
        {
            result.Error = "The query matched no rows.";
            return result;
        }

        return string.IsNullOrWhiteSpace(query.GroupBy)
            ? RawXy(query, rows, result)
            : Grouped(query, rows, result);
    }

    // ------------------------------------------------------------------ modes

    private static QueryResult RawXy(ChartQuery query, List<Dictionary<string, string?>> rows, QueryResult result)
    {
        foreach (var row in rows)
        {
            string? x = Field(row, query.XField);
            double? y = ToNumber(Field(row, query.YField));
            if (x is null || y is null)
            {
                continue;
            }

            result.Labels.Add(x);
            result.Values.Add(y);
        }

        if (result.Labels.Count == 0)
        {
            result.Error = $"No rows had both '{query.XField}' and a numeric '{query.YField}'.";
            return result;
        }

        ApplySort(query, result);
        ApplyLimit(query, result);
        result.Success = true;
        return result;
    }

    private static QueryResult Grouped(ChartQuery query, List<Dictionary<string, string?>> rows, QueryResult result)
    {
        // Category order is first-seen, then whatever sort is asked for.
        var categories = new List<string>();
        var buckets = new Dictionary<string, List<Dictionary<string, string?>>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            string? key = Field(row, query.GroupBy);
            if (key is null)
            {
                continue;
            }

            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<Dictionary<string, string?>>();
                buckets[key] = bucket;
                categories.Add(key);
            }

            bucket.Add(row);
        }

        if (categories.Count == 0)
        {
            result.Error = $"Field '{query.GroupBy}' was not found in the data.";
            return result;
        }

        // A weekday axis is expected to show the whole week, Monday to Sunday, including
        // days with no rows — a chart missing Sunday reads as if Sunday did not exist.
        if (IsWeekdayGrouping(query.GroupBy))
        {
            var week = new[]
            {
                DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday,
                DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday
            };

            categories = week
                .Select(day => CultureInfo.InvariantCulture.DateTimeFormat.GetDayName(day))
                .ToList();

            foreach (string day in categories)
            {
                if (!buckets.ContainsKey(day))
                {
                    buckets[day] = new List<Dictionary<string, string?>>();
                }
            }
        }

        if (string.IsNullOrWhiteSpace(query.SeriesBy))
        {
            foreach (string category in categories)
            {
                result.Labels.Add(category);
                result.Values.Add(Reduce(buckets[category], query.Aggregate));
            }
        }
        else
        {
            var seriesNames = new List<string>();
            foreach (var row in rows)
            {
                string? name = Field(row, query.SeriesBy);
                if (name is not null && !seriesNames.Contains(name, StringComparer.Ordinal))
                {
                    seriesNames.Add(name);
                }
            }

            result.Labels.AddRange(categories);

            foreach (string name in seriesNames)
            {
                var series = new ChartSeries { Name = name };
                foreach (string category in categories)
                {
                    var subset = buckets[category]
                        .Where(row => string.Equals(Field(row, query.SeriesBy), name, StringComparison.Ordinal))
                        .ToList();

                    series.Values.Add(subset.Count == 0 ? 0 : Reduce(subset, query.Aggregate));
                }

                result.Series.Add(series);
            }

            // Keep a flat total on Values so the three-slice BIL fields stay meaningful.
            foreach (string category in categories)
            {
                result.Values.Add(Reduce(buckets[category], query.Aggregate));
            }
        }

        ApplySort(query, result);
        ApplyLimit(query, result);
        result.Success = true;
        return result;
    }

    private static double? Reduce(List<Dictionary<string, string?>> rows, QueryAggregate? aggregate)
    {
        string fn = (aggregate?.Fn ?? "count").Trim().ToLowerInvariant();
        string? fieldName = aggregate?.Field;

        if (fn is "count" or "" or "none")
        {
            if (string.IsNullOrWhiteSpace(fieldName) || fieldName == "*")
            {
                return rows.Count;
            }

            return rows.Count(row => !string.IsNullOrEmpty(Field(row, fieldName)));
        }

        if (fn is "count_distinct" or "countdistinct" or "distinct")
        {
            return rows.Select(row => Field(row, fieldName))
                       .Where(value => !string.IsNullOrEmpty(value))
                       .Distinct(StringComparer.Ordinal)
                       .Count();
        }

        var numbers = rows.Select(row => ToNumber(Field(row, fieldName)))
                          .Where(value => value.HasValue)
                          .Select(value => value!.Value)
                          .ToList();

        if (numbers.Count == 0)
        {
            return null;
        }

        return fn switch
        {
            "sum" => numbers.Sum(),
            "avg" or "average" or "mean" => numbers.Average(),
            "min" => numbers.Min(),
            "max" => numbers.Max(),
            _ => numbers.Sum()
        };
    }

    // ----------------------------------------------------------------- helpers

    private static bool IsWeekdayGrouping(string? groupBy)
    {
        if (string.IsNullOrWhiteSpace(groupBy))
        {
            return false;
        }

        string key = groupBy.Trim();
        int open = key.IndexOf('(');
        if (open <= 0 || !key.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        string function = key[..open].Trim().ToUpperInvariant();
        return function is "WEEKDAY" or "DAYNAME";
    }

    private static string? CheckComplete(EvalTable table) =>
        table.TotalRowCount > table.Rows.Count
            ? $"Table '{table.Name}' arrived truncated ({table.Rows.Count} of {table.TotalRowCount} rows); " +
              "refusing to aggregate a sample."
            : null;

    private static EvalTable? FindTable(List<EvalTable> tables, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string wanted = name.Trim();
        return tables.FirstOrDefault(t => string.Equals(t.Name, wanted, StringComparison.OrdinalIgnoreCase));
    }

    private static List<Dictionary<string, string?>> Materialise(EvalTable table)
    {
        var rows = new List<Dictionary<string, string?>>(table.Rows.Count);

        foreach (var values in table.Rows)
        {
            var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < table.Columns.Count && i < values.Count; i++)
            {
                string column = table.Columns[i];
                row[column] = values[i];
                row[$"{table.Name}.{column}"] = values[i];
            }

            rows.Add(row);
        }

        return rows;
    }

    private static List<Dictionary<string, string?>> InnerJoin(
        List<Dictionary<string, string?>> left,
        List<Dictionary<string, string?>> right,
        string leftField,
        string rightField,
        out string? error)
    {
        error = null;

        var index = new Dictionary<string, List<Dictionary<string, string?>>>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in right)
        {
            string? key = Field(row, rightField);
            if (key is null)
            {
                continue;
            }

            if (!index.TryGetValue(key, out var bucket))
            {
                bucket = new List<Dictionary<string, string?>>();
                index[key] = bucket;
            }

            bucket.Add(row);
        }

        var joined = new List<Dictionary<string, string?>>();
        foreach (var row in left)
        {
            string? key = Field(row, leftField);
            if (key is null || !index.TryGetValue(key, out var matches))
            {
                continue;
            }

            foreach (var match in matches)
            {
                if (joined.Count >= MaxJoinRows)
                {
                    error = "The join produced too many rows to aggregate in process.";
                    return joined;
                }

                var merged = new Dictionary<string, string?>(row, StringComparer.OrdinalIgnoreCase);
                foreach (var pair in match)
                {
                    merged[pair.Key] = pair.Value;
                }

                joined.Add(merged);
            }
        }

        if (joined.Count == 0)
        {
            error = $"The join on {leftField} = {rightField} matched no rows.";
        }

        return joined;
    }

    /// <summary>
    /// Reads a field, tolerating "Table.Col", bare "Col", SQL aliases like "T1.Col", and
    /// the handful of scalar functions models reach for: YEAR/MONTH/DAY/LOWER/UPPER.
    /// Date binning matters — VisEval's own ground truth groups dates by year.
    /// </summary>
    private static string? Field(Dictionary<string, string?> row, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string key = name.Trim();

        int open = key.IndexOf('(');
        if (open > 0 && key.EndsWith(")", StringComparison.Ordinal))
        {
            string function = key[..open].Trim().ToUpperInvariant();
            string inner = key[(open + 1)..^1].Trim();
            return ApplyFunction(function, ReadRaw(row, inner));
        }

        return ReadRaw(row, key);
    }

    private static string? ReadRaw(Dictionary<string, string?> row, string key)
    {
        if (row.TryGetValue(key, out var value))
        {
            return value;
        }

        int dot = key.LastIndexOf('.');
        if (dot >= 0 && dot < key.Length - 1 && row.TryGetValue(key[(dot + 1)..], out value))
        {
            return value;
        }

        return null;
    }

    private static string? ApplyFunction(string function, string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        switch (function)
        {
            case "YEAR":
            case "MONTH":
            case "DAY":
            case "WEEKDAY":
            case "DAYNAME":
                if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                {
                    var format = CultureInfo.InvariantCulture.DateTimeFormat;
                    return function switch
                    {
                        "YEAR" => parsed.Year.ToString(CultureInfo.InvariantCulture),
                        // Month and weekday bins are compared by name ("Feb", "Thur"),
                        // so return the full name and let prefix matching do the rest.
                        "MONTH" => format.GetMonthName(parsed.Month),
                        "WEEKDAY" or "DAYNAME" => format.GetDayName(parsed.DayOfWeek),
                        _ => parsed.Day.ToString(CultureInfo.InvariantCulture)
                    };
                }

                // "2016-09-27 ..." that DateTime could not parse still starts with the year.
                if (function == "YEAR" && raw.Length >= 4 && raw[..4].All(char.IsDigit))
                {
                    return raw[..4];
                }

                return null;

            case "LOWER":
                return raw.ToLowerInvariant();
            case "UPPER":
                return raw.ToUpperInvariant();
            default:
                // Unknown wrapper (COUNT(x), SUM(x), ...) — treat it as the bare column.
                return raw;
        }
    }

    private static bool Matches(Dictionary<string, string?> row, QueryFilter filter)
    {
        string? actual = Field(row, filter.Field);
        string expected = filter.Value ?? string.Empty;
        string op = (filter.Op ?? "=").Trim();

        if (op is "contains")
        {
            return actual is not null && actual.Contains(expected, StringComparison.OrdinalIgnoreCase);
        }

        double? a = ToNumber(actual);
        double? b = ToNumber(expected);
        if (a.HasValue && b.HasValue)
        {
            return op switch
            {
                "=" or "==" => a.Value == b.Value,
                "!=" or "<>" => a.Value != b.Value,
                ">" => a.Value > b.Value,
                ">=" => a.Value >= b.Value,
                "<" => a.Value < b.Value,
                "<=" => a.Value <= b.Value,
                _ => true
            };
        }

        return op switch
        {
            "!=" or "<>" => !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase),
            _ => string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static void ApplySort(ChartQuery query, QueryResult result)
    {
        if (string.IsNullOrWhiteSpace(query.Sort) || result.Labels.Count != result.Values.Count)
        {
            return;
        }

        string sort = query.Sort.Trim().ToLowerInvariant();
        bool byY = sort.StartsWith("y", StringComparison.Ordinal);
        bool descending = sort.Contains("desc", StringComparison.Ordinal);

        var order = Enumerable.Range(0, result.Labels.Count).ToList();
        order.Sort((i, j) =>
        {
            int comparison;
            if (byY)
            {
                comparison = Nullable.Compare(result.Values[i], result.Values[j]);
            }
            else
            {
                double? li = ToNumber(result.Labels[i]);
                double? lj = ToNumber(result.Labels[j]);
                comparison = li.HasValue && lj.HasValue
                    ? li.Value.CompareTo(lj.Value)
                    : string.Compare(result.Labels[i], result.Labels[j], StringComparison.OrdinalIgnoreCase);
            }

            return descending ? -comparison : comparison;
        });

        result.Labels = order.Select(i => result.Labels[i]).ToList();
        result.Values = order.Select(i => result.Values[i]).ToList();
        foreach (var series in result.Series)
        {
            if (series.Values.Count == order.Count)
            {
                series.Values = order.Select(i => series.Values[i]).ToList();
            }
        }
    }

    private static void ApplyLimit(ChartQuery query, QueryResult result)
    {
        if (query.Limit is null || query.Limit.Value <= 0 || result.Labels.Count <= query.Limit.Value)
        {
            return;
        }

        int take = query.Limit.Value;
        result.Labels = result.Labels.Take(take).ToList();
        result.Values = result.Values.Take(take).ToList();
        foreach (var series in result.Series)
        {
            series.Values = series.Values.Take(take).ToList();
        }
    }

    private static double? ToNumber(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return double.TryParse(
            text.Trim().Replace(",", string.Empty),
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out double value)
            ? value
            : null;
    }
}
