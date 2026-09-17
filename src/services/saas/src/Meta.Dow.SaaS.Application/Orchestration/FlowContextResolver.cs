using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowRuntimeContext
{
    public JsonObject Input { get; } = new();

    public JsonObject Sys { get; set; } = new();

    public JsonObject Vars { get; } = new();

    public JsonObject Results { get; } = new();

    /// <summary>
    /// 出参短名索引：outputName → 值（后写覆盖）。下游可直接用 level，而不必 results.nodeId.level。
    /// </summary>
    public JsonObject Pubs { get; } = new();

    public SystemContextSnapshot? Snapshot { get; set; }

    public Dictionary<string, JsonNode?> FlatVarsLegacy { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public static class FlowContextResolver
{
    public static object? ResolvePath(string? path, FlowRuntimeContext ctx)
    {
        return ToClr(ResolvePathNode(path, ctx));
    }

    /// <summary>
    /// 按路径取 JsonNode（保留 array/object），供最终出参 map/aggregate 使用。
    /// 勿经 ToClr：复杂类型会被压成 JSON 字符串导致 End 无法识别为数组。
    /// </summary>
    public static JsonNode? ResolvePathNode(string? path, FlowRuntimeContext ctx)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var token = path.Trim();

        if (DateExpressionEvaluator.LooksLikeDateExpression(token) &&
            (token.Contains('+') || token.Contains('-') || token.Contains('|') ||
             token.StartsWith("sys.Now", StringComparison.OrdinalIgnoreCase) ||
             token.StartsWith("sys.Today", StringComparison.OrdinalIgnoreCase) ||
             token.StartsWith("sys.Month", StringComparison.OrdinalIgnoreCase) ||
             token.StartsWith("sys.Year", StringComparison.OrdinalIgnoreCase) ||
             token.StartsWith("sys.Week", StringComparison.OrdinalIgnoreCase) ||
             token.StartsWith("sys.Yester", StringComparison.OrdinalIgnoreCase) ||
             token.StartsWith("sys.Tomor", StringComparison.OrdinalIgnoreCase)))
        {
            if (ctx.Snapshot == null)
            {
                throw new UserFriendlyException("System snapshot is required for date expression.");
            }

            if (!token.Contains('+') && !token.Contains('-') && !token.Contains('|') &&
                token.StartsWith("sys.", StringComparison.OrdinalIgnoreCase))
            {
                var key = token["sys.".Length..];
                if (ctx.Sys.TryGetPropertyValue(key, out var snap) ||
                    ctx.Sys.TryGetPropertyValue(DateExpressionEvaluator.NormalizeAnchor(token)["sys.".Length..], out snap))
                {
                    return snap?.DeepClone();
                }
            }

            var dt = DateExpressionEvaluator.Evaluate(token, ctx.Snapshot.Now, ctx.Snapshot.TimeZone);
            return JsonValue.Create(dt.ToString("o"));
        }

        if (token.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(true);
        }

        if (token.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return JsonValue.Create(false);
        }

        if ((token.StartsWith('"') && token.EndsWith('"')) || (token.StartsWith('\'') && token.EndsWith('\'')))
        {
            return JsonValue.Create(token[1..^1]);
        }

        if (decimal.TryParse(token, NumberStyles.Any, CultureInfo.InvariantCulture, out var number))
        {
            return JsonValue.Create(number);
        }

        if (token.StartsWith("input.", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(ctx.Input, token["input.".Length..])?.DeepClone();
        }

        if (token.StartsWith("sys.", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(ctx.Sys, token["sys.".Length..])?.DeepClone();
        }

        if (token.StartsWith("vars.", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(ctx.Vars, token["vars.".Length..])?.DeepClone();
        }

        if (token.StartsWith("results.", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(ctx.Results, token["results.".Length..])?.DeepClone();
        }

        if (token.Contains('.') &&
            !token.StartsWith("input.", StringComparison.OrdinalIgnoreCase) &&
            !token.StartsWith("sys.", StringComparison.OrdinalIgnoreCase) &&
            !token.StartsWith("vars.", StringComparison.OrdinalIgnoreCase))
        {
            if (TryResolveListPathSugar(token, ctx, out var sugar))
            {
                return sugar;
            }

            var head = token.Split('.', 2)[0];
            if (ctx.Results[head] != null ||
                ctx.Results.Any(x => string.Equals(x.Key, head, StringComparison.OrdinalIgnoreCase)))
            {
                return GetByPath(ctx.Results, token)?.DeepClone();
            }

            if (ctx.Pubs[head] != null ||
                ctx.Pubs.Any(x => string.Equals(x.Key, head, StringComparison.OrdinalIgnoreCase)))
            {
                return GetByPath(ctx.Pubs, token)?.DeepClone();
            }
        }

        if (ctx.Pubs.TryGetPropertyValue(token, out var published))
        {
            return published?.DeepClone();
        }

        foreach (var kv in ctx.Pubs)
        {
            if (string.Equals(kv.Key, token, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value?.DeepClone();
            }
        }

        if (ctx.FlatVarsLegacy.TryGetValue(token, out var legacy))
        {
            return legacy?.DeepClone();
        }

        return GetByPath(ctx.Vars, token)?.DeepClone();
    }

    /// <summary>
    /// list 路径糖：rows.count / rows.first.order / rows.max.date / rows.sum.amount …
    /// </summary>
    private static bool TryResolveListPathSugar(string token, FlowRuntimeContext ctx, out JsonNode? result)
    {
        result = null;
        var parts = token.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
        {
            return false;
        }

        static bool IsOp(string s) =>
            s.Equals("count", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("first", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("last", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("sum", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("avg", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("min", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("max", StringComparison.OrdinalIgnoreCase);

        string listPath;
        string op;
        string? fieldPath = null;

        if (parts[^1].Equals("count", StringComparison.OrdinalIgnoreCase))
        {
            listPath = string.Join('.', parts[..^1]);
            op = "count";
        }
        else if (parts.Length >= 3 && IsOp(parts[^2]))
        {
            listPath = string.Join('.', parts[..^2]);
            op = parts[^2];
            fieldPath = parts[^1];
        }
        else
        {
            return false;
        }

        // 避免递归进糖：直接从 pubs/results/vars 取数组
        var listNode = ResolveListBaseNode(listPath, ctx);
        if (listNode is not JsonArray array)
        {
            return false;
        }

        result = AggregateArray(array, op, fieldPath);
        return true;
    }

    private static JsonNode? ResolveListBaseNode(string listPath, FlowRuntimeContext ctx)
    {
        if (string.IsNullOrWhiteSpace(listPath))
        {
            return null;
        }

        if (listPath.StartsWith("input.", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(ctx.Input, listPath["input.".Length..]);
        }

        if (listPath.StartsWith("results.", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(ctx.Results, listPath["results.".Length..]);
        }

        if (listPath.StartsWith("vars.", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(ctx.Vars, listPath["vars.".Length..]);
        }

        if (listPath.Contains('.'))
        {
            var head = listPath.Split('.', 2)[0];
            if (ctx.Results[head] != null ||
                ctx.Results.Any(x => string.Equals(x.Key, head, StringComparison.OrdinalIgnoreCase)))
            {
                return GetByPath(ctx.Results, listPath);
            }

            if (ctx.Pubs[head] != null ||
                ctx.Pubs.Any(x => string.Equals(x.Key, head, StringComparison.OrdinalIgnoreCase)))
            {
                return GetByPath(ctx.Pubs, listPath);
            }
        }

        if (ctx.Pubs.TryGetPropertyValue(listPath, out var pub))
        {
            return pub;
        }

        foreach (var kv in ctx.Pubs)
        {
            if (string.Equals(kv.Key, listPath, StringComparison.OrdinalIgnoreCase))
            {
                return kv.Value;
            }
        }

        return GetByPath(ctx.Vars, listPath);
    }

    internal static JsonNode? AggregateArray(JsonArray array, string op, string? fieldPath)
    {
        var opNorm = op.Trim().ToLowerInvariant();
        switch (opNorm)
        {
            case "count":
                return JsonValue.Create(array.Count);
            case "first":
            {
                if (array.Count == 0) return null;
                return string.IsNullOrWhiteSpace(fieldPath)
                    ? array[0]?.DeepClone()
                    : GetByPath(array[0], fieldPath!)?.DeepClone();
            }
            case "last":
            {
                if (array.Count == 0) return null;
                return string.IsNullOrWhiteSpace(fieldPath)
                    ? array[^1]?.DeepClone()
                    : GetByPath(array[^1], fieldPath!)?.DeepClone();
            }
            case "sum":
            case "avg":
            case "min":
            case "max":
            {
                if (string.IsNullOrWhiteSpace(fieldPath))
                {
                    return null;
                }

                var numbers = new List<decimal>();
                foreach (var item in array)
                {
                    var raw = GetByPath(item, fieldPath);
                    if (TryDecimal(raw, out var d))
                    {
                        numbers.Add(d);
                    }
                }

                if (numbers.Count == 0)
                {
                    return opNorm is "sum" or "avg" ? JsonValue.Create(0) : null;
                }

                return opNorm switch
                {
                    "sum" => JsonValue.Create(numbers.Sum()),
                    "avg" => JsonValue.Create(numbers.Average()),
                    "min" => JsonValue.Create(numbers.Min()),
                    "max" => JsonValue.Create(numbers.Max()),
                    _ => null
                };
            }
            default:
                return null;
        }
    }

    private static bool TryDecimal(JsonNode? value, out decimal number)
    {
        number = 0;
        if (value is null)
        {
            return false;
        }

        if (value is JsonValue jv)
        {
            if (jv.TryGetValue<decimal>(out var d))
            {
                number = d;
                return true;
            }

            if (jv.TryGetValue<double>(out var dbl))
            {
                number = (decimal)dbl;
                return true;
            }

            if (jv.TryGetValue<int>(out var i))
            {
                number = i;
                return true;
            }

            if (jv.TryGetValue<long>(out var l))
            {
                number = l;
                return true;
            }

            if (jv.TryGetValue<string>(out var s) &&
                decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out d))
            {
                number = d;
                return true;
            }
        }

        return decimal.TryParse(value.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out number);
    }

    public static JsonNode? ResolveBindingFrom(JsonNode? from, FlowRuntimeContext ctx)
    {
        if (from == null)
        {
            return null;
        }

        if (from is JsonObject obj && obj.TryGetPropertyValue("literal", out var literal))
        {
            return literal?.DeepClone();
        }

        if (from is JsonValue jv && jv.TryGetValue<string>(out var s))
        {
            return ResolvePathNode(s, ctx) ?? ToJsonNode(ResolvePath(s, ctx));
        }

        return from.DeepClone();
    }

    public static JsonObject MapNodeInputs(IEnumerable<FlowNodeBindingDsl>? bindings, FlowRuntimeContext ctx)
    {
        var nodeInput = new JsonObject();
        if (bindings == null)
        {
            return nodeInput;
        }

        foreach (var binding in bindings)
        {
            var value = ResolveBindingFrom(binding.From, ctx);
            if (value == null && binding.Required)
            {
                throw new UserFriendlyException($"Required node input '{binding.Name}' is missing.");
            }

            nodeInput[binding.Name] = value?.DeepClone();
        }

        return nodeInput;
    }

    public static void ApplyNodeOutputs(
        IEnumerable<FlowNodeBindingDsl>? bindings,
        JsonNode? rawResult,
        string nodeId,
        FlowRuntimeContext ctx,
        string? nodeRef = null,
        string? resultRoot = null
    )
    {
        var key = string.IsNullOrWhiteSpace(nodeRef) ? nodeId : nodeRef.Trim();
        ctx.Results[key] = rawResult?.DeepClone() ?? new JsonObject();
        // 兼容仍用内部 Id 引用
        if (!string.Equals(key, nodeId, StringComparison.Ordinal))
        {
            ctx.Results[nodeId] = ctx.Results[key]?.DeepClone();
        }

        if (bindings == null)
        {
            return;
        }

        foreach (var binding in bindings)
        {
            var fromPath = binding.Name;
            if (binding.From is JsonValue jv && jv.TryGetValue<string>(out var path))
            {
                fromPath = path;
            }

            var value = ResolveFromRaw(rawResult, fromPath, resultRoot);

            // list/object → map.item 投影（支持嵌套）；type 空时按源形态推断，避免对象被当成 array
            if (binding.Map?.Item is { Count: > 0 })
            {
                var mapType = string.IsNullOrWhiteSpace(binding.Type)
                    ? value is JsonArray ? "array" : "object"
                    : binding.Type;
                value = FinalOutputAssembler.ProjectWithMap(value, binding.Map, mapType, ctx);
            }

            var to = binding.To;
            if (string.IsNullOrWhiteSpace(to))
            {
                to = $"results.{key}.{binding.Name}";
            }

            WriteTo(ctx, to!, value);
            EnsureResultsNode(ctx, key)[binding.Name] = value?.DeepClone();
            if (!string.Equals(key, nodeId, StringComparison.Ordinal))
            {
                EnsureResultsNode(ctx, nodeId)[binding.Name] = value?.DeepClone();
            }

            // 短名发布：下游直接写 level / {{level}}
            if (!string.IsNullOrWhiteSpace(binding.Name))
            {
                ctx.Pubs[binding.Name] = value?.DeepClone();
            }
        }
    }

    public static void WriteTo(FlowRuntimeContext ctx, string target, JsonNode? value)
    {
        if (target.StartsWith("vars.", StringComparison.OrdinalIgnoreCase))
        {
            SetByPath(ctx.Vars, target["vars.".Length..], value);
            ctx.FlatVarsLegacy[target["vars.".Length..].Split('.')[0]] = GetByPath(ctx.Vars, target["vars.".Length..].Split('.')[0]);
            return;
        }

        if (target.StartsWith("results.", StringComparison.OrdinalIgnoreCase))
        {
            SetByPath(ctx.Results, target["results.".Length..], value);
            return;
        }

        SetByPath(ctx.Vars, target, value);
        ctx.FlatVarsLegacy[target.Split('.')[0]] = GetByPath(ctx.Vars, target.Split('.')[0]);
    }

    public static string SerializeContext(FlowRuntimeContext ctx)
    {
        var root = new JsonObject
        {
            ["input"] = ctx.Input.DeepClone(),
            ["sys"] = ctx.Sys.DeepClone(),
            ["vars"] = ctx.Vars.DeepClone(),
            ["results"] = ctx.Results.DeepClone(),
            ["pubs"] = ctx.Pubs.DeepClone()
        };
        return root.ToJsonString(new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });
    }

    public static JsonNode? GetByPath(JsonNode? root, string path)
    {
        if (root == null || string.IsNullOrWhiteSpace(path))
        {
            return root;
        }

        var parts = SplitPath(path);
        JsonNode? current = root;
        foreach (var part in parts)
        {
            if (current is JsonObject obj)
            {
                if (!obj.TryGetPropertyValue(part, out current))
                {
                    // case-insensitive fallback
                    current = obj.FirstOrDefault(x => string.Equals(x.Key, part, StringComparison.OrdinalIgnoreCase)).Value;
                    if (current == null)
                    {
                        return null;
                    }
                }
            }
            else if (current is JsonArray arr && int.TryParse(part.Trim('[', ']'), out var idx))
            {
                current = idx >= 0 && idx < arr.Count ? arr[idx] : null;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static void SetByPath(JsonObject root, string path, JsonNode? value)
    {
        var parts = SplitPath(path);
        if (parts.Count == 0)
        {
            return;
        }

        JsonObject current = root;
        for (var i = 0; i < parts.Count - 1; i++)
        {
            if (current[parts[i]] is not JsonObject next)
            {
                next = new JsonObject();
                current[parts[i]] = next;
            }

            current = next;
        }

        current[parts[^1]] = value?.DeepClone();
    }

    private static JsonObject EnsureResultsNode(FlowRuntimeContext ctx, string nodeId)
    {
        if (ctx.Results[nodeId] is JsonObject obj)
        {
            return obj;
        }

        var created = new JsonObject();
        ctx.Results[nodeId] = created;
        return created;
    }

    private static JsonNode? ResolveFromRaw(JsonNode? raw, string path, string? resultRoot = null)
    {
        if (string.IsNullOrWhiteSpace(path) || path is "." )
        {
            return ResolveResultRootNode(raw, resultRoot)?.DeepClone() ?? raw?.DeepClone();
        }

        if (path.Equals("return", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(raw, "return")?.DeepClone() ?? raw?.DeepClone();
        }

        // 信封绝对路径：始终相对 raw
        if (IsAbsoluteEnvelopePath(path))
        {
            var abs = path.StartsWith("response.", StringComparison.OrdinalIgnoreCase)
                ? path["response.".Length..]
                : path.StartsWith("return.", StringComparison.OrdinalIgnoreCase)
                    ? path["return.".Length..]
                    : path;
            return GetByPath(raw, abs)?.DeepClone();
        }

        // ① 相对 resultRoot
        var root = ResolveResultRootNode(raw, resultRoot);
        if (root != null)
        {
            var relative = path.StartsWith("return.", StringComparison.OrdinalIgnoreCase)
                ? GetByPath(root, path["return.".Length..])
                : GetByPath(root, path);
            if (relative != null)
            {
                return relative.DeepClone();
            }
        }

        // ② 回退 raw 根
        if (path.StartsWith("return.", StringComparison.OrdinalIgnoreCase))
        {
            return GetByPath(raw, path["return.".Length..])?.DeepClone();
        }

        return GetByPath(raw, path)?.DeepClone();
    }

    private static JsonNode? ResolveResultRootNode(JsonNode? raw, string? resultRoot)
    {
        if (string.IsNullOrWhiteSpace(resultRoot) || resultRoot is "." or "$")
        {
            return raw;
        }

        return GetByPath(raw, resultRoot.Trim());
    }

    /// <summary>
    /// statusCode / body / body.xxx / response.xxx / rows… 视为信封绝对路径。
    /// </summary>
    private static bool IsAbsoluteEnvelopePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.Equals("statusCode", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("status", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("body", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("response", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("rows", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("rowCount", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return path.StartsWith("body.", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("response.", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("rows.", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("rows[", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> SplitPath(string path)
    {
        var parts = new List<string>();
        var buffer = "";
        for (var i = 0; i < path.Length; i++)
        {
            var c = path[i];
            if (c == '.')
            {
                if (buffer.Length > 0)
                {
                    parts.Add(buffer);
                    buffer = "";
                }
            }
            else if (c == '[')
            {
                if (buffer.Length > 0)
                {
                    parts.Add(buffer);
                    buffer = "";
                }

                var end = path.IndexOf(']', i);
                if (end < 0)
                {
                    buffer += c;
                }
                else
                {
                    parts.Add(path[(i + 1)..end]);
                    i = end;
                }
            }
            else
            {
                buffer += c;
            }
        }

        if (buffer.Length > 0)
        {
            parts.Add(buffer);
        }

        return parts;
    }

    public static object? ToClr(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonValue value when value.TryGetValue<bool>(out var b) => b,
            JsonValue value when value.TryGetValue<decimal>(out var d) => d,
            JsonValue value when value.TryGetValue<string>(out var s) => s,
            JsonValue value => value.ToString(),
            _ => node.ToJsonString()
        };
    }

    public static JsonNode? ToJsonNode(object? value)
    {
        return value switch
        {
            null => null,
            JsonNode n => n.DeepClone(),
            bool b => JsonValue.Create(b),
            string s => JsonValue.Create(s),
            IConvertible c when decimal.TryParse(Convert.ToString(c, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var d)
                && c is not string => JsonValue.Create(d),
            _ => JsonValue.Create(Convert.ToString(value, CultureInfo.InvariantCulture))
        };
    }

    public static string ResolveTemplate(string template, FlowRuntimeContext ctx)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        return Regex.Replace(
            template,
            @"\{\{\s*(?<path>[^}]+)\s*\}\}",
            m =>
            {
                var value = ResolvePath(m.Groups["path"].Value.Trim(), ctx);
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        );
    }
}
