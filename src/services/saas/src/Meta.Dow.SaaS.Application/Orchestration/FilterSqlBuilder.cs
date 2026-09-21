using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

public sealed class FilterSqlResult
{
    public string WhereSql { get; init; } = "1=1";

    public JsonObject Args { get; init; } = [];
}

public static class FilterSqlBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Regex TokenRegex = new(
        @"\s*(?<tok>and|or|not|\(|\)|\d+)\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public static FilterSqlResult Build(
        string provider,
        TableDefinition table,
        FilterDef? designerFilter,
        JsonNode? runtimeFilters,
        Guid? tenantId
    )
    {
        var args = new JsonObject();
        var predicates = new List<string>();
        var colMap = table.Columns.ToDictionary(c => c.Name, StringComparer.OrdinalIgnoreCase);

        if (colMap.ContainsKey("TenantId"))
        {
            if (tenantId.HasValue)
            {
                args["tenantId"] = tenantId.Value.ToString();
                predicates.Add($"{SqlDialect.QuoteIdent(provider, "TenantId")} = {SqlDialect.ParamName(provider, "tenantId")}");
            }
            else
            {
                predicates.Add($"{SqlDialect.QuoteIdent(provider, "TenantId")} IS NULL");
            }
        }

        if (colMap.ContainsKey("IsDeleted"))
        {
            args["isDeleted"] = false;
            predicates.Add($"{SqlDialect.QuoteIdent(provider, "IsDeleted")} = {SqlDialect.ParamName(provider, "isDeleted")}");
        }

        if (designerFilter?.DataScope != null)
        {
            var scopeNode = JsonSerializer.SerializeToNode(designerFilter.DataScope, JsonOptions);
            var scopeSql = CompileNode(provider, scopeNode, colMap, args, "ds", 0);
            if (!string.IsNullOrWhiteSpace(scopeSql))
            {
                predicates.Add(scopeSql);
            }
        }

        if (designerFilter?.Items is { Count: > 0 })
        {
            var hidden = designerFilter.Items.Where(x => !x.Exposed).ToList();
            if (hidden.Count > 0)
            {
                var hiddenPreds = new Dictionary<int, string>();
                foreach (var item in hidden)
                {
                    hiddenPreds[item.No] = BuildPredicate(
                        provider,
                        item.Left,
                        item.Op,
                        ResolveDesignerValue(item),
                        colMap,
                        args,
                        $"d{item.No}"
                    );
                }

                var combine = string.IsNullOrWhiteSpace(designerFilter.Combine)
                    ? string.Join(" and ", hidden.Select(x => x.No))
                    : designerFilter.Combine;
                predicates.Add("(" + CombineToSql(combine, hiddenPreds) + ")");
            }
        }

        var runtimeNode = runtimeFilters;
        if (runtimeFilters is JsonObject wrap &&
            wrap["filter"] is JsonNode nested)
        {
            runtimeNode = nested;
        }

        if (IsGroupTree(runtimeNode))
        {
            var treeSql = CompileNode(provider, runtimeNode, colMap, args, "rt", 0);
            if (!string.IsNullOrWhiteSpace(treeSql))
            {
                predicates.Add(treeSql);
            }
        }
        else
        {
            var runtime = ParseRuntime(runtimeNode);
            if (runtime.Items.Count > 0)
            {
                var runtimePreds = new Dictionary<int, string>();
                foreach (var item in runtime.Items)
                {
                    runtimePreds[item.No] = BuildPredicate(
                        provider,
                        item.Left,
                        item.Op,
                        item.Right,
                        colMap,
                        args,
                        $"r{item.No}"
                    );
                }

                var combine = string.IsNullOrWhiteSpace(runtime.Combine)
                    ? string.Join(" and ", runtime.Items.Select(x => x.No))
                    : runtime.Combine;
                predicates.Add("(" + CombineToSql(combine, runtimePreds) + ")");
            }
        }

        return new FilterSqlResult
        {
            WhereSql = predicates.Count == 0 ? "1=1" : string.Join(" AND ", predicates),
            Args = args
        };
    }

    private static JsonNode? ResolveDesignerValue(FilterItemDef item)
    {
        if (string.Equals(item.ValueSource, "literal", StringComparison.OrdinalIgnoreCase))
        {
            return item.Literal == null ? null : JsonValue.Create(item.Literal);
        }

        return null;
    }

    private static (List<FlowConditionItemDsl> Items, string? Combine) ParseRuntime(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return ([], null);
        }

        var combine = obj["combine"]?.GetValue<string>();
        var items = new List<FlowConditionItemDsl>();
        if (obj["items"] is JsonArray arr)
        {
            foreach (var el in arr)
            {
                if (el is not JsonObject it)
                {
                    continue;
                }

                items.Add(new FlowConditionItemDsl
                {
                    No = JsonNodeNumbers.ToInt32(it["no"]) ?? items.Count + 1,
                    Left = it["left"]?.GetValue<string>(),
                    Op = it["op"]?.GetValue<string>() ?? "eq",
                    Right = it["right"]?.DeepClone()
                });
            }
        }

        return (items, combine);
    }

    private static bool IsGroupTree(JsonNode? node)
    {
        if (node is not JsonObject obj)
        {
            return false;
        }

        if (obj["children"] is JsonArray)
        {
            return true;
        }

        var kind = obj["kind"]?.GetValue<string>();
        return kind is "group" or "rule";
    }

    private static string? CompileNode(
        string provider,
        JsonNode? node,
        IReadOnlyDictionary<string, TableColumn> colMap,
        JsonObject args,
        string prefix,
        int depth
    )
    {
        if (node is not JsonObject obj)
        {
            return null;
        }

        if (depth > OrchestrationConsts.MaxFilterGroupDepth)
        {
            throw new UserFriendlyException(
                $"Filter group nesting exceeds the limit of {OrchestrationConsts.MaxFilterGroupDepth}."
            );
        }

        var kind = obj["kind"]?.GetValue<string>();
        var isRule = kind is "rule" ||
                     (obj["left"] != null && obj["children"] is not JsonArray);
        if (isRule)
        {
            var left = obj["left"]?.GetValue<string>();
            var op = obj["op"]?.GetValue<string>() ?? "eq";
            var right = obj["right"];
            if (IsBlank(right) &&
                (op ?? "").Trim().ToLowerInvariant() is not ("isempty" or "isnotempty"))
            {
                return null;
            }

            return BuildPredicate(provider, left, op, right, colMap, args, prefix);
        }

        var groupOp = (obj["op"]?.GetValue<string>() ?? "and").Trim().ToLowerInvariant();
        if (groupOp is not ("and" or "or"))
        {
            groupOp = "and";
        }

        var children = obj["children"] as JsonArray ?? [];
        var parts = new List<string>();
        for (var i = 0; i < children.Count; i++)
        {
            var sql = CompileNode(
                provider,
                children[i],
                colMap,
                args,
                prefix + "g" + i.ToString(CultureInfo.InvariantCulture),
                depth + 1
            );
            if (!string.IsNullOrWhiteSpace(sql))
            {
                parts.Add(sql);
            }
        }

        if (parts.Count == 0)
        {
            return null;
        }

        if (parts.Count == 1)
        {
            return parts[0];
        }

        var joiner = groupOp == "or" ? " OR " : " AND ";
        return "(" + string.Join(joiner, parts) + ")";
    }

    private static string BuildPredicate(
        string provider,
        string? left,
        string? op,
        JsonNode? right,
        IReadOnlyDictionary<string, TableColumn> colMap,
        JsonObject args,
        string prefix
    )
    {
        if (string.IsNullOrWhiteSpace(left) || !colMap.ContainsKey(left))
        {
            throw new UserFriendlyException($"Filter field is not allowed: {left}");
        }

        var col = SqlDialect.QuoteIdent(provider, colMap[left].Name);
        var normalized = (op ?? "eq").Trim().ToLowerInvariant();
        if (normalized is "isempty")
        {
            return SqlDialect.IsEmptySql(provider, col, colMap[left].PlatformType);
        }

        if (normalized is "isnotempty")
        {
            return SqlDialect.IsNotEmptySql(provider, col, colMap[left].PlatformType);
        }

        if (IsBlank(right))
        {
            return "1=1";
        }

        var platform = TablePlatformType.Normalize(colMap[left].PlatformType);
        if (platform is not (TablePlatformType.String or TablePlatformType.Text or TablePlatformType.Enum) &&
            normalized is "contains" or "notcontains" or "startswith")
        {
            normalized = "eq";
        }

        var p1 = prefix + "a";
        var p2 = prefix + "b";

        switch (normalized)
        {
            case "eq":
                args[p1] = Clone(right);
                return $"{col} = {SqlDialect.ParamName(provider, p1)}";
            case "ne":
                args[p1] = Clone(right);
                return $"{col} <> {SqlDialect.ParamName(provider, p1)}";
            case "gt":
                args[p1] = Clone(right);
                return $"{col} > {SqlDialect.ParamName(provider, p1)}";
            case "gte":
                args[p1] = Clone(right);
                return $"{col} >= {SqlDialect.ParamName(provider, p1)}";
            case "lt":
                args[p1] = Clone(right);
                return $"{col} < {SqlDialect.ParamName(provider, p1)}";
            case "lte":
                args[p1] = Clone(right);
                return $"{col} <= {SqlDialect.ParamName(provider, p1)}";
            case "contains":
            case "notcontains":
            {
                var text = right?.ToString() ?? "";
                args[p1] = "%" + text + "%";
                var like = $"{col} {LikeOperator(provider)} {SqlDialect.ParamName(provider, p1)}";
                return normalized == "contains" ? like : $"NOT ({like})";
            }
            case "startswith":
                args[p1] = (right?.ToString() ?? "") + "%";
                return $"{col} {LikeOperator(provider)} {SqlDialect.ParamName(provider, p1)}";
            case "between":
            {
                JsonNode? min = right;
                JsonNode? max = null;
                if (right is JsonArray arr && arr.Count >= 2)
                {
                    min = arr[0];
                    max = arr[1];
                }

                args[p1] = Clone(min);
                args[p2] = Clone(max);
                return $"{col} >= {SqlDialect.ParamName(provider, p1)} AND {col} < {SqlDialect.ParamName(provider, p2)}";
            }
            case "in":
            case "notin":
            {
                var values = right as JsonArray ?? [];
                if (values.Count == 0)
                {
                    return normalized == "in" ? "1=0" : "1=1";
                }

                var names = new List<string>();
                for (var i = 0; i < values.Count; i++)
                {
                    var pn = prefix + "i" + i.ToString(CultureInfo.InvariantCulture);
                    args[pn] = Clone(values[i]);
                    names.Add(SqlDialect.ParamName(provider, pn));
                }

                var clause = $"{col} IN ({string.Join(", ", names)})";
                return normalized == "in" ? clause : $"NOT ({clause})";
            }
            default:
                throw new UserFriendlyException($"Unsupported filter operator: {op}");
        }
    }

    private static string LikeOperator(string provider) =>
        SqlDialect.NormalizeProvider(provider) == DataSourceProvider.Postgres ? "ILIKE" : "LIKE";

    private static JsonNode? Clone(JsonNode? node) => node?.DeepClone();

    private static bool IsBlank(JsonNode? node)
    {
        if (node is null)
        {
            return true;
        }

        if (node is JsonValue value)
        {
            return value.GetValueKind() switch
            {
                JsonValueKind.Null => true,
                JsonValueKind.String => string.IsNullOrWhiteSpace(value.GetValue<string>()),
                _ => false
            };
        }

        if (node is JsonArray arr)
        {
            return arr.Count == 0 || arr.All(IsBlank);
        }

        return false;
    }

    private static string CombineToSql(string? combine, IReadOnlyDictionary<int, string> predicates)
    {
        if (string.IsNullOrWhiteSpace(combine))
        {
            return string.Join(" AND ", predicates.Values);
        }

        ConditionCombineEvaluator.Validate(combine, predicates.Keys);
        var tokens = Tokenize(combine);
        var index = 0;
        var sql = ParseOr(tokens, ref index, predicates);
        if (index != tokens.Count)
        {
            throw new UserFriendlyException($"Invalid combine expression: {combine}");
        }

        return sql;
    }

    private static List<string> Tokenize(string combine)
    {
        var list = new List<string>();
        var pos = 0;
        while (pos < combine.Length)
        {
            var m = TokenRegex.Match(combine, pos);
            if (!m.Success || m.Index != pos)
            {
                throw new UserFriendlyException($"Invalid combine expression: {combine}");
            }

            list.Add(m.Groups["tok"].Value.ToLowerInvariant());
            pos = m.Index + m.Length;
        }

        return list;
    }

    private static string ParseOr(List<string> tokens, ref int index, IReadOnlyDictionary<int, string> items)
    {
        var left = ParseAnd(tokens, ref index, items);
        var sb = new StringBuilder(left);
        while (index < tokens.Count && tokens[index] == "or")
        {
            index++;
            sb.Append(" OR ");
            sb.Append(ParseAnd(tokens, ref index, items));
        }

        return sb.ToString();
    }

    private static string ParseAnd(List<string> tokens, ref int index, IReadOnlyDictionary<int, string> items)
    {
        var left = ParseUnary(tokens, ref index, items);
        var sb = new StringBuilder(left);
        while (index < tokens.Count && tokens[index] == "and")
        {
            index++;
            sb.Append(" AND ");
            sb.Append(ParseUnary(tokens, ref index, items));
        }

        return sb.ToString();
    }

    private static string ParseUnary(List<string> tokens, ref int index, IReadOnlyDictionary<int, string> items)
    {
        if (index >= tokens.Count)
        {
            throw new UserFriendlyException("Unexpected end of combine expression.");
        }

        if (tokens[index] == "not")
        {
            index++;
            return "NOT (" + ParseUnary(tokens, ref index, items) + ")";
        }

        if (tokens[index] == "(")
        {
            index++;
            var inner = ParseOr(tokens, ref index, items);
            if (index >= tokens.Count || tokens[index] != ")")
            {
                throw new UserFriendlyException("Missing ')' in combine expression.");
            }

            index++;
            return "(" + inner + ")";
        }

        if (int.TryParse(tokens[index], out var no))
        {
            index++;
            if (!items.TryGetValue(no, out var pred))
            {
                throw new UserFriendlyException($"Condition no {no} not found.");
            }

            return pred;
        }

        throw new UserFriendlyException($"Unexpected token '{tokens[index]}' in combine expression.");
    }
}
