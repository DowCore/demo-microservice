using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Meta.Dow.SaaS.Orchestration;

/// <summary>
/// DSL 结构校验：把"未知字段被静默忽略"变成发布期错误。
///
/// <para><b>缺陷 A1 的机理</b>：<c>JsonSerializer</c> 默认忽略无法映射到属性的字段，于是
/// <c>txMode</c>、拼错的 <c>subFlowKeyy</c>、笔误的 <c>asyncJobId</c> 全部无声消失。
/// 使用方以为配了，实际没配——直接违反 tradeoffs 第二原则"静默零容忍：配置被接受就必须生效"。</para>
///
/// <para><b>为什么用反射生成白名单</b>：手写字段清单必然与 <c>FlowDslModels</c> 漂移
/// （这正是本仓已经发生过的文档失真模式）。这里直接从模型类型反射出可写属性，
/// <b>模型加字段白名单自动跟随</b>，永远不会漏。</para>
///
/// <para><b>边界</b>：属性类型是 <c>JsonNode</c> / <c>object</c> 等不透明类型时，
/// 其内部结构无法从 CLR 类型推出，按「不透明」处理，不做递归校验。</para>
///
/// <para><b>字段名 vs 取值</b>：<see cref="ValidateJson"/> 靠反射只能回答"字段名存不存在"，
/// 回答不了"这个<b>取值</b>有没有消费点"（如 <c>payloadMode=array</c> 会静默退化为整包 inputs）。
/// 这类约束由 <see cref="ValidateSemantics"/> 在反序列化后按模型校验。</para>
/// </summary>
public static class DslSchemaValidator
{
    /// <summary>
    /// 模型里存在但<b>没有任何消费点</b>的字段。配了等于没配，且比未知字段更有欺骗性
    /// ——用户会以为功能已生效。这些字段一律在发布期拒绝，并提示替代方案。
    /// </summary>
    private static readonly Dictionary<string, string> UnimplementedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["sql"] = "没有独立 Sql 节点；SQL 请走资源 CRUD 节点，或 Code 节点内的 db.execute / db.query。",
        ["transform"] = "出参变换请用 from（路径表达式）、map.item 或 aggregate。",
        ["asyncJobId"] = "作业 Id 由引擎在async 模式下自动产出，无需（也不能）在 DSL 里手写；下游用 Wait 节点引用它。",
        ["previewAsRole"] = "按角色预览尚未实现；出参可见性由 visibleTo 在运行时统一裁剪。",
    };

    /// <summary>
    /// <c>payloadMode</c> 的合法取值。<c>array</c> 曾写在模型的注释里，但
    /// <c>BuildRabbitPayload</c> 从不按 array 分支处理——配了它会静默退化成"整包 inputs"，
    /// 与"配了等于没配"等价，故显式拒绝。
    /// </summary>
    private static readonly HashSet<string> KnownPayloadModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "object",
        "raw"
    };

    /// <summary>
    /// 语义级校验：走反射白名单管不到的"取值"约束。
    /// 反射只能回答"这个字段名存不存在"，回答不了"这个值有没有消费点"。
    /// </summary>
    public static void ValidateSemantics(FlowDslDocument dsl)
    {
        var problems = new List<string>();

        foreach (var node in dsl.Nodes ?? [])
        {
            if (string.IsNullOrWhiteSpace(node.PayloadMode))
            {
                continue;
            }

            var mode = node.PayloadMode.Trim();
            if (!KnownPayloadModes.Contains(mode))
            {
                problems.Add(
                    $"`nodes.{node.Id}.payloadMode` = '{node.PayloadMode}' 不受支持" +
                    $"（受支持：{string.Join(" / ", KnownPayloadModes)}；省略即 object）。" +
                    "注意 `array` 不会按数组处理，只会静默退化为整包 inputs。"
                );
            }
        }

        if (problems.Count > 0)
        {
            throw new Volo.Abp.UserFriendlyException(
                $"DSL 含无法生效的取值，已阻止发布：{string.Join("；", problems.Take(8))}。" +
                "取值拼写错误与未实现取值都不会静默忽略——请修正后重新发布。"
            );
        }
    }

    private static readonly Dictionary<Type, HashSet<string>> AllowedNamesCache = new();

    /// <summary>
    /// 校验 DSL 原始 JSON。发现未知字段或已知但未实现的字段即抛出。
    /// </summary>
    /// <param name="dslJson">DSL 原始文本。</param>
    /// <exception cref="Volo.Abp.UserFriendlyException">存在未知或未实现字段。</exception>
    public static void ValidateJson(string? dslJson)
    {
        if (string.IsNullOrWhiteSpace(dslJson))
        {
            return;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(dslJson);
        }
        catch (JsonException ex)
        {
            throw new Volo.Abp.UserFriendlyException($"DSL 不是合法 JSON：{ex.Message}");
        }

        if (root is not JsonObject rootObject)
        {
            return;
        }

        var problems = new List<string>();
        Walk(rootObject, typeof(FlowDslDocument), "dsl", problems);

        if (problems.Count > 0)
        {
            var detail = string.Join("；", problems.Take(8));
            var more = problems.Count > 8 ? $"（另有 {problems.Count - 8} 处未列出）" : "";
            throw new Volo.Abp.UserFriendlyException(
                $"DSL 含无法生效的字段，已阻止发布：{detail}{more}。" +
                "字段拼写错误与未实现字段都不会静默忽略——请修正后重新发布。"
            );
        }
    }

    /// <summary>递归比对 JSON 对象与其 CLR 模型类型的字段集合。</summary>
    private static void Walk(JsonObject node, Type modelType, string path, List<string> problems)
    {
        var allowed = GetAllowedNames(modelType);

        foreach (var (key, value) in node)
        {
            if (UnimplementedFields.TryGetValue(key, out var hint))
            {
                problems.Add($"`{path}.{key}` 尚未实现：{hint}");
                continue;
            }

            if (allowed.Contains(key))
            {
                var property = FindProperty(modelType, key);
                if (property != null)
                {
                    WalkValue(value, property.PropertyType, $"{path}.{key}", problems);
                }

                continue;
            }

            problems.Add(
                $"`{path}.{key}` 不是有效字段" +
                Suggest(key, allowed)
            );
        }
    }

    /// <summary>按属性的声明类型递归校验值。</summary>
    private static void WalkValue(JsonNode? value, Type type, string path, List<string> problems)
    {
        if (value == null)
        {
            return;
        }

        // JsonNode / object 等不透明类型：不递归。
        if (type == typeof(JsonNode) || type == typeof(object) || type == typeof(JsonElement))
        {
            return;
        }

        if (value is JsonObject obj)
        {
            // 自身就是 JsonNode 的属性（如 node.Header 之类）直接放行。
            if (type == typeof(JsonObject))
            {
                return;
            }

            Walk(obj, type, path, problems);
            return;
        }

        if (value is JsonArray array)
        {
            var elementType = GetElementType(type);
            if (elementType == null || elementType == typeof(JsonNode) || elementType == typeof(object))
            {
                return;
            }

            if (elementType == typeof(JsonObject))
            {
                return;
            }

            for (var i = 0; i < array.Count; i++)
            {
                WalkValue(array[i], elementType, $"{path}[{i}]", problems);
            }
        }
    }

    /// <summary>取可写属性名的 camelCase 集合（带缓存）。</summary>
    private static HashSet<string> GetAllowedNames(Type type)
    {
        if (AllowedNamesCache.TryGetValue(type, out var cached))
        {
            return cached;
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanWrite)
            {
                continue;
            }

            names.Add(ToCamelCase(p.Name));
        }

        AllowedNamesCache[type] = names;
        return names;
    }

    private static PropertyInfo? FindProperty(Type type, string camelName)
    {
        foreach (var p in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.CanWrite && ToCamelCase(p.Name).Equals(camelName, StringComparison.OrdinalIgnoreCase))
            {
                return p;
            }
        }

        return null;
    }

    /// <summary>展开 <c>List&lt;T&gt;</c> / <c>IList&lt;T&gt;</c> / <c>T[]</c> 的元素类型。</summary>
    private static Type? GetElementType(Type type)
    {
        if (type.IsArray)
        {
            return type.GetElementType();
        }

        if (type.IsGenericType)
        {
            var args = type.GetGenericArguments();
            if (args.Length == 1)
            {
                return args[0];
            }
        }

        var iface = type.GetInterfaces()
            .FirstOrDefault(x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>));

        return iface?.GetGenericArguments()[0];
    }

    private static string ToCamelCase(string pascal)
    {
        if (string.IsNullOrEmpty(pascal) || !char.IsUpper(pascal[0]))
        {
            return pascal;
        }

        return char.ToLowerInvariant(pascal[0]) + pascal[1..];
    }

    /// <summary>对拼写接近的合法字段给出提示，避免用户盲猜。</summary>
    private static string Suggest(string unknown, HashSet<string> allowed)
    {
        var best = allowed
            .Select(a => (Name: a, Distance: Levenshtein(unknown.ToLowerInvariant(), a.ToLowerInvariant())))
            .Where(x => x.Distance is > 0 and <= 3)
            .OrderBy(x => x.Distance)
            .Select(x => x.Name)
            .FirstOrDefault();

        return best == null ? "" : $"，是否想写 `{best}`？";
    }

    private static int Levenshtein(string a, string b)
    {
        if (a.Length == 0)
        {
            return b.Length;
        }

        if (b.Length == 0)
        {
            return a.Length;
        }

        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            prev[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }
}
