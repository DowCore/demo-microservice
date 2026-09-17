using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Volo.Abp;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// 按 End.finalOutputs（或根级 outputs）组装最终 data：映射 / list map / aggregate + 字段可见性。
/// </summary>
public static class FinalOutputAssembler
{
    private static readonly Regex TemplateToken = new(@"\{\{\s*([^}]+?)\s*\}\}", RegexOptions.Compiled);

    public sealed class AssembleResult
    {
        /// <summary>API data：默认 JsonObject；出参 promote 时可直接为 JsonArray / JsonObject。</summary>
        public JsonNode Data { get; init; } = new JsonObject();

        public List<string> VisibleFields { get; init; } = [];

        public int ContractFieldCount { get; init; }
    }

    public static AssembleResult Assemble(
        IReadOnlyList<FlowOutputParameterDsl> schema,
        FlowRuntimeContext ctx,
        ISet<string> roles,
        ISet<string> permissions,
        bool bypass
    )
    {
        var promoteCount = schema.Count(x => x.Promote);
        if (promoteCount > 1)
        {
            throw new UserFriendlyException(
                "finalOutputs 最多只能有一项 promote=true（data 将直接等于该项的值）。"
            );
        }

        if (promoteCount == 1)
        {
            return AssemblePromotedRoot(schema, ctx, roles, permissions, bypass);
        }

        var data = new JsonObject();
        var visible = new List<string>();
        var contractCount = 0;

        foreach (var output in schema)
        {
            if (string.IsNullOrWhiteSpace(output.Name) && output.Group?.Promote != true)
            {
                continue;
            }

            contractCount++;
            if (!bypass && !OutputVisibilityFilter.IsVisible(output.VisibleTo, roles, permissions))
            {
                continue;
            }

            var fieldPath = string.IsNullOrWhiteSpace(output.Name) ? "$" : output.Name;
            var value = ResolveOutputValue(output, ctx, roles, permissions, bypass, fieldPath, visible);

            if (TryPromoteGroupToRoot(output, value, data, visible))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(output.Name))
            {
                continue;
            }

            data[output.Name] = value?.DeepClone();
            if (!visible.Contains(output.Name, StringComparer.Ordinal))
            {
                visible.Add(output.Name);
            }
        }

        return new AssembleResult
        {
            Data = data,
            VisibleFields = visible,
            ContractFieldCount = contractCount
        };
    }

    /// <summary>
    /// promote=true：响应 data 直接为该出参值（常见 data=[] / data={}）。
    /// </summary>
    private static AssembleResult AssemblePromotedRoot(
        IReadOnlyList<FlowOutputParameterDsl> schema,
        FlowRuntimeContext ctx,
        ISet<string> roles,
        ISet<string> permissions,
        bool bypass
    )
    {
        var namedCount = schema.Count(x =>
            x.Promote || !string.IsNullOrWhiteSpace(x.Name) || x.Group?.Promote == true
        );
        if (namedCount > 1)
        {
            throw new UserFriendlyException(
                "promote=true 时 finalOutputs 只能配置这一项，才能得到 { \"data\": [...] } 或 { \"data\": {...} }。"
            );
        }

        var output = schema.First(x => x.Promote);
        var visible = new List<string>();
        var fieldPath = string.IsNullOrWhiteSpace(output.Name) ? "$" : output.Name;

        if (!bypass && !OutputVisibilityFilter.IsVisible(output.VisibleTo, roles, permissions))
        {
            // 不可见时：array 给空数组，其余给空对象，避免 data:null 歧义
            var empty =
                (output.Type ?? "").Equals("array", StringComparison.OrdinalIgnoreCase)
                    ? (JsonNode)new JsonArray()
                    : new JsonObject();
            return new AssembleResult
            {
                Data = empty,
                VisibleFields = visible,
                ContractFieldCount = 1
            };
        }

        var value = ResolveOutputValue(output, ctx, roles, permissions, bypass, fieldPath, visible);
        if (value == null)
        {
            value = (output.Type ?? "").Equals("array", StringComparison.OrdinalIgnoreCase)
                ? new JsonArray()
                : new JsonObject();
        }
        else if (value is not JsonArray and not JsonObject)
        {
            throw new UserFriendlyException(
                $"Output '{output.Name}' promote=true 时值须为 array 或 object，当前为标量。请检查 from / map。"
            );
        }

        if (!string.IsNullOrWhiteSpace(output.Name) &&
            !visible.Contains(output.Name, StringComparer.Ordinal))
        {
            visible.Add(output.Name);
        }
        else if (string.IsNullOrWhiteSpace(output.Name) && !visible.Contains("$", StringComparer.Ordinal))
        {
            visible.Add("$");
        }

        return new AssembleResult
        {
            Data = value.DeepClone()!,
            VisibleFields = visible,
            ContractFieldCount = 1
        };
    }

    /// <summary>
    /// group.promote：单组对象字段提升到 data 根级。
    /// </summary>
    private static bool TryPromoteGroupToRoot(
        FlowOutputParameterDsl output,
        JsonNode? value,
        JsonObject data,
        List<string> visible
    )
    {
        if (output.Group?.Promote != true)
        {
            return false;
        }

        JsonObject? obj = null;
        if (value is JsonArray arr)
        {
            if (arr.Count == 0)
            {
                return true;
            }

            if (arr.Count > 1)
            {
                throw new UserFriendlyException(
                    $"Output '{output.Name}' group.promote 仅支持恰好 1 个分组，当前有 {arr.Count} 组。请关闭「提升为顶层」或收窄数据。"
                );
            }

            obj = arr[0] as JsonObject;
        }
        else if (value is JsonObject single)
        {
            obj = single;
        }

        if (obj == null)
        {
            return true;
        }

        foreach (var prop in obj)
        {
            if (string.IsNullOrWhiteSpace(prop.Key))
            {
                continue;
            }

            data[prop.Key] = prop.Value?.DeepClone();
            if (!visible.Contains(prop.Key, StringComparer.Ordinal))
            {
                visible.Add(prop.Key);
            }
        }

        return true;
    }

    private static JsonNode? ResolveOutputValue(
        FlowOutputParameterDsl output,
        FlowRuntimeContext ctx,
        ISet<string> roles,
        ISet<string> permissions,
        bool bypass,
        string fieldPath,
        List<string> visiblePaths
    )
    {
        if (output.Group != null && output.Group.By != null)
        {
            return ApplyGroup(output, ctx, roles, permissions, bypass, fieldPath, visiblePaths);
        }

        if (output.Aggregate != null && !string.IsNullOrWhiteSpace(output.Aggregate.Op))
        {
            return ApplyAggregate(output, ctx);
        }

        var source = ResolveSource(output.From, output.Name, ctx);

        if (output.Map?.Item is { Count: > 0 })
        {
            var type = string.IsNullOrWhiteSpace(output.Type) ? "string" : output.Type.Trim();
            if (type.Equals("object", StringComparison.OrdinalIgnoreCase))
            {
                return MapItem(
                    output.Map.Item,
                    source,
                    ctx,
                    roles,
                    permissions,
                    bypass,
                    fieldPath,
                    visiblePaths
                );
            }

            var array = AsArray(source, output.Name, output.Wrap);
            var mapped = new JsonArray();
            foreach (var item in array)
            {
                var row = MapItem(output.Map.Item, item, ctx, roles, permissions, bypass, fieldPath, visiblePaths);
                mapped.Add(row);
            }

            return mapped;
        }

        if (!string.IsNullOrWhiteSpace(output.Wrap) &&
            output.Wrap.Equals("array", StringComparison.OrdinalIgnoreCase) &&
            source is not JsonArray)
        {
            return new JsonArray { source?.DeepClone() };
        }

        return source?.DeepClone();
    }

    private static JsonArray ApplyGroup(
        FlowOutputParameterDsl output,
        FlowRuntimeContext ctx,
        ISet<string> roles,
        ISet<string> permissions,
        bool bypass,
        string fieldPath,
        List<string> visiblePaths
    )
    {
        var group = output.Group!;
        var keys = NormalizeGroupBy(group.By);
        if (keys.Count == 0)
        {
            throw new UserFriendlyException($"Output '{output.Name}' group.by is required.");
        }

        var source = ResolveSource(output.From, output.Name, ctx);
        var rows = AsArray(source, output.Name, output.Wrap);
        var buckets = new Dictionary<string, List<JsonNode?>>(StringComparer.Ordinal);

        foreach (var row in rows)
        {
            var key = BuildGroupKey(row, keys);
            if (!buckets.TryGetValue(key, out var list))
            {
                list = [];
                buckets[key] = list;
            }

            list.Add(row);
        }

        var result = new JsonArray();
        foreach (var (_, groupRows) in buckets)
        {
            var obj = new JsonObject();
            var pickRow = groupRows[0];

            if (group.Header is { Count: > 0 })
            {
                foreach (var header in group.Header)
                {
                    if (string.IsNullOrWhiteSpace(header.Name))
                    {
                        continue;
                    }

                    var path = $"{fieldPath}[].{header.Name}";
                    if (!bypass && !OutputVisibilityFilter.IsVisible(header.VisibleTo, roles, permissions))
                    {
                        continue;
                    }

                    JsonNode? value;
                    if (header.Aggregate != null && !string.IsNullOrWhiteSpace(header.Aggregate.Op))
                    {
                        var arr = new JsonArray();
                        foreach (var r in groupRows)
                        {
                            arr.Add(r?.DeepClone());
                        }

                        value = FlowContextResolver.AggregateArray(
                            arr,
                            header.Aggregate.Op,
                            header.Aggregate.Path ?? header.From ?? header.Name
                        );
                    }
                    else
                    {
                        var take = string.IsNullOrWhiteSpace(header.Take) ? "first" : header.Take.Trim();
                        var row =
                            take.Equals("last", StringComparison.OrdinalIgnoreCase)
                                ? groupRows[^1]
                                : groupRows[0];
                        var from = string.IsNullOrWhiteSpace(header.From) ? header.Name : header.From;
                        value = ResolveRelative(JsonValue.Create(from), row, ctx);
                    }

                    obj[header.Name] = value?.DeepClone();
                    if (!visiblePaths.Contains(path, StringComparer.Ordinal))
                    {
                        visiblePaths.Add(path);
                    }
                }
            }
            else
            {
                // 未配 header 时，默认把 by 键写入结果
                foreach (var key in keys)
                {
                    obj[key] = ResolveRelative(JsonValue.Create(key), pickRow, ctx)?.DeepClone();
                }
            }

            if (group.Children != null &&
                !string.IsNullOrWhiteSpace(group.Children.Name) &&
                (bypass || OutputVisibilityFilter.IsVisible(group.Children.VisibleTo, roles, permissions)))
            {
                var childName = group.Children.Name;
                var childPath = $"{fieldPath}[].{childName}";
                var childArr = new JsonArray();
                var mapItems = group.Children.Map?.Item;
                foreach (var row in groupRows)
                {
                    if (mapItems is { Count: > 0 })
                    {
                        childArr.Add(
                            MapItem(mapItems, row, ctx, roles, permissions, bypass, childPath, visiblePaths)
                        );
                    }
                    else
                    {
                        childArr.Add(row?.DeepClone());
                    }
                }

                obj[childName] = childArr;
                if (!visiblePaths.Contains(childPath, StringComparer.Ordinal))
                {
                    visiblePaths.Add(childPath);
                }
            }

            result.Add(obj);
        }

        return result;
    }

    private static List<string> NormalizeGroupBy(JsonNode? by)
    {
        var keys = new List<string>();
        if (by is JsonArray arr)
        {
            foreach (var item in arr)
            {
                var s = item?.ToString()?.Trim();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    keys.Add(s);
                }
            }
        }
        else if (by is JsonValue jv)
        {
            var s = jv.ToString()?.Trim();
            if (!string.IsNullOrWhiteSpace(s))
            {
                keys.Add(s);
            }
        }

        return keys;
    }

    private static string BuildGroupKey(JsonNode? row, IReadOnlyList<string> keys)
    {
        var parts = new List<string>(keys.Count);
        foreach (var key in keys)
        {
            var v = FlowContextResolver.GetByPath(row, key);
            parts.Add(v?.ToString() ?? "");
        }

        return string.Join("\u001f", parts);
    }

    private static JsonObject MapItem(
        IEnumerable<FlowOutputMapItemDsl> items,
        JsonNode? row,
        FlowRuntimeContext ctx,
        ISet<string> roles,
        ISet<string> permissions,
        bool bypass,
        string parentPath,
        List<string> visiblePaths
    )
    {
        var obj = new JsonObject();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            var path = $"{parentPath}[].{item.Name}";
            if (!bypass && !OutputVisibilityFilter.IsVisible(item.VisibleTo, roles, permissions))
            {
                continue;
            }

            var resolved = ResolveRelative(item.From, row, ctx);
            obj[item.Name] = ProjectMappedValue(
                item,
                resolved,
                ctx,
                roles,
                permissions,
                bypass,
                path,
                visiblePaths
            );
            if (!visiblePaths.Contains(path, StringComparer.Ordinal))
            {
                visiblePaths.Add(path);
            }
        }

        return obj;
    }

    /// <summary>
    /// 按字段 type + 嵌套 map 投影：标量透传；array/object 可继续展开 map.item。
    /// </summary>
    private static JsonNode? ProjectMappedValue(
        FlowOutputMapItemDsl item,
        JsonNode? source,
        FlowRuntimeContext ctx,
        ISet<string> roles,
        ISet<string> permissions,
        bool bypass,
        string fieldPath,
        List<string> visiblePaths
    )
    {
        var type = string.IsNullOrWhiteSpace(item.Type) ? "string" : item.Type.Trim();
        var hasNestedMap = item.Map?.Item is { Count: > 0 };

        if (!hasNestedMap)
        {
            return source?.DeepClone();
        }

        if (type.Equals("array", StringComparison.OrdinalIgnoreCase))
        {
            var array = AsArray(source, item.Name, wrap: null);
            var mapped = new JsonArray();
            foreach (var el in array)
            {
                mapped.Add(
                    MapItem(
                        item.Map!.Item!,
                        el,
                        ctx,
                        roles,
                        permissions,
                        bypass,
                        fieldPath,
                        visiblePaths
                    )
                );
            }

            return mapped;
        }

        if (type.Equals("object", StringComparison.OrdinalIgnoreCase))
        {
            // 对象字段投影：把 source 当作「当前行」再走 map.item
            return MapItem(
                item.Map!.Item!,
                source,
                ctx,
                roles,
                permissions,
                bypass,
                fieldPath,
                visiblePaths
            );
        }

        // 标量类型若误配了 map，忽略嵌套，透传值
        return source?.DeepClone();
    }

    /// <summary>
    /// 节点出参投影：按 type 选择 array 逐行 / object 字段映射（含嵌套 map）。
    /// type 为空时：源是 JsonArray → array，否则按 object（避免误把对象当数组）。
    /// </summary>
    public static JsonNode? ProjectWithMap(
        JsonNode? source,
        FlowOutputMapDsl? map,
        string? type,
        FlowRuntimeContext ctx
    )
    {
        if (map?.Item is not { Count: > 0 })
        {
            return source?.DeepClone();
        }

        var t = string.IsNullOrWhiteSpace(type)
            ? source is JsonArray ? "array" : "object"
            : type.Trim();
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visible = new List<string>();

        if (t.Equals("object", StringComparison.OrdinalIgnoreCase))
        {
            return MapItem(map.Item, source, ctx, roles, permissions, bypass: true, "item", visible);
        }

        var array = AsArray(source, "map", wrap: null);
        var mapped = new JsonArray();
        foreach (var item in array)
        {
            mapped.Add(MapItem(map.Item, item, ctx, roles, permissions, bypass: true, "item", visible));
        }

        return mapped;
    }

    /// <summary>
    /// 按字段表投影为对象（Rabbit 消息体 / End object map 同语义），绝不按 array 解释。
    /// </summary>
    public static JsonObject ProjectObjectFields(
        IEnumerable<FlowOutputMapItemDsl> items,
        JsonNode? sourceRow,
        FlowRuntimeContext ctx
    )
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visible = new List<string>();
        return MapItem(items, sourceRow, ctx, roles, permissions, bypass: true, "payload", visible);
    }

    /// <summary>
    /// 节点出参 list 投影（无字段可见性过滤）。
    /// </summary>
    public static JsonNode? MapArrayItems(
        JsonNode? source,
        FlowOutputMapDsl? map,
        FlowRuntimeContext ctx
    )
    {
        return ProjectWithMap(source, map, "array", ctx);
    }

    private static JsonNode? ApplyAggregate(FlowOutputParameterDsl output, FlowRuntimeContext ctx)
    {
        var source = ResolveSource(output.From, output.Name, ctx);
        var array = AsArray(source, output.Name, output.Wrap);
        var op = output.Aggregate!.Op.Trim().ToLowerInvariant();
        var path = output.Aggregate.Path?.Trim();

        switch (op)
        {
            case "count":
                return JsonValue.Create(array.Count);
            case "first":
                return array.Count > 0 ? array[0]?.DeepClone() : null;
            case "last":
                return array.Count > 0 ? array[^1]?.DeepClone() : null;
            case "sum":
            case "avg":
            case "min":
            case "max":
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    throw new UserFriendlyException($"Aggregate '{op}' on '{output.Name}' requires path.");
                }

                var numbers = new List<decimal>();
                foreach (var item in array)
                {
                    var raw = GetRelativeClr(item, path, ctx);
                    if (TryDecimal(raw, out var d))
                    {
                        numbers.Add(d);
                    }
                }

                if (numbers.Count == 0)
                {
                    return op is "sum" or "avg" ? JsonValue.Create(0) : null;
                }

                return op switch
                {
                    "sum" => JsonValue.Create(numbers.Sum()),
                    "avg" => JsonValue.Create(numbers.Average()),
                    "min" => JsonValue.Create(numbers.Min()),
                    "max" => JsonValue.Create(numbers.Max()),
                    _ => null
                };
            }
            default:
                throw new UserFriendlyException($"Unsupported aggregate op '{output.Aggregate.Op}'.");
        }
    }

    private static JsonNode? ResolveSource(string? from, string name, FlowRuntimeContext ctx)
    {
        var path = string.IsNullOrWhiteSpace(from) ? name : from;
        return FlowContextResolver.ResolvePathNode(path, ctx);
    }

    private static JsonArray AsArray(JsonNode? source, string fieldName, string? wrap)
    {
        if (source is JsonArray arr)
        {
            return arr;
        }

        // 兼容：历史上 ToClr 把 array 压成 JSON 字符串
        if (source is JsonValue jv && jv.TryGetValue<string>(out var json) &&
            !string.IsNullOrWhiteSpace(json) && json.TrimStart().StartsWith('['))
        {
            try
            {
                if (JsonNode.Parse(json) is JsonArray parsed)
                {
                    return parsed;
                }
            }
            catch
            {
                // fall through
            }
        }

        if (!string.IsNullOrWhiteSpace(wrap) && wrap.Equals("array", StringComparison.OrdinalIgnoreCase))
        {
            return source == null ? [] : new JsonArray { source.DeepClone() };
        }

        if (source == null)
        {
            return [];
        }

        throw new UserFriendlyException(
            $"Output '{fieldName}' expects an array source, but got {(source is JsonObject ? "object" : source.GetType().Name)}."
        );
    }

    /// <summary>
    /// 相对当前行解析；带命名空间或引用名时走全局；支持 literal / template。
    /// </summary>
    public static JsonNode? ResolveRelative(JsonNode? from, JsonNode? row, FlowRuntimeContext ctx)
    {
        if (from == null)
        {
            return null;
        }

        if (from is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("literal", out var literal))
            {
                return literal?.DeepClone();
            }

            if (obj.TryGetPropertyValue("template", out var templateNode) &&
                templateNode?.GetValue<string>() is { } template)
            {
                return JsonValue.Create(ResolveItemTemplate(template, row, ctx));
            }
        }

        if (from is JsonValue jv && jv.TryGetValue<string>(out var path) && !string.IsNullOrWhiteSpace(path))
        {
            if (IsGlobalPath(path, ctx))
            {
                return FlowContextResolver.ToJsonNode(FlowContextResolver.ResolvePath(path, ctx));
            }

            return FlowContextResolver.ToJsonNode(GetRelativeClr(row, path, ctx));
        }

        return from.DeepClone();
    }

    private static bool IsGlobalPath(string path, FlowRuntimeContext ctx)
    {
        var token = path.Trim();
        if (token.StartsWith("input.", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("sys.", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("vars.", StringComparison.OrdinalIgnoreCase) ||
            token.StartsWith("results.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!token.Contains('.'))
        {
            return false;
        }

        var head = token.Split('.', 2)[0];
        if (ctx.Results[head] != null ||
            ctx.Results.Any(x => string.Equals(x.Key, head, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (ctx.Pubs[head] != null ||
            ctx.Pubs.Any(x => string.Equals(x.Key, head, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    private static object? GetRelativeClr(JsonNode? row, string path, FlowRuntimeContext ctx)
    {
        var token = path.Trim();
        if (token is "." or "$" or "")
        {
            return FlowContextResolver.ToClr(row);
        }

        if (token.StartsWith("item.", StringComparison.OrdinalIgnoreCase))
        {
            token = token["item.".Length..];
        }
        else if (token.StartsWith("row.", StringComparison.OrdinalIgnoreCase))
        {
            token = token["row.".Length..];
        }

        if (row is JsonObject or JsonArray)
        {
            return FlowContextResolver.ToClr(FlowContextResolver.GetByPath(row, token));
        }

        if (row is JsonValue)
        {
            return FlowContextResolver.ToClr(row);
        }

        // 回退全局（兼容）
        return FlowContextResolver.ResolvePath(path, ctx);
    }

    private static string ResolveItemTemplate(string template, JsonNode? row, FlowRuntimeContext ctx)
    {
        return TemplateToken.Replace(template, m =>
        {
            var key = m.Groups[1].Value.Trim();
            var value = IsGlobalPath(key, ctx)
                ? FlowContextResolver.ResolvePath(key, ctx)
                : GetRelativeClr(row, key, ctx);
            return value?.ToString() ?? "";
        });
    }

    private static bool TryDecimal(object? value, out decimal number)
    {
        switch (value)
        {
            case null:
                number = 0;
                return false;
            case decimal d:
                number = d;
                return true;
            case int i:
                number = i;
                return true;
            case long l:
                number = l;
                return true;
            case double dbl:
                number = (decimal)dbl;
                return true;
            case float f:
                number = (decimal)f;
                return true;
            case JsonValue jv when jv.TryGetValue<decimal>(out var jd):
                number = jd;
                return true;
            default:
                return decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out number);
        }
    }

    public static IEnumerable<string> CollectPermissionNames(IEnumerable<FlowOutputParameterDsl> schema)
    {
        foreach (var output in schema)
        {
            if (output.VisibleTo?.Permissions != null)
            {
                foreach (var p in output.VisibleTo.Permissions)
                {
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        yield return p;
                    }
                }
            }

            foreach (var p in CollectMapPermissions(output.Map))
            {
                yield return p;
            }

            if (output.Group?.Children?.Map != null)
            {
                foreach (var p in CollectMapPermissions(output.Group.Children.Map))
                {
                    yield return p;
                }
            }
        }
    }

    private static IEnumerable<string> CollectMapPermissions(FlowOutputMapDsl? map)
    {
        if (map?.Item == null)
        {
            yield break;
        }

        foreach (var item in map.Item)
        {
            if (item.VisibleTo?.Permissions != null)
            {
                foreach (var p in item.VisibleTo.Permissions)
                {
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        yield return p;
                    }
                }
            }

            foreach (var nested in CollectMapPermissions(item.Map))
            {
                yield return nested;
            }
        }
    }
}
