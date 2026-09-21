using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meta.Dow.SaaS.Orchestration;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.DependencyInjection;

namespace Meta.Dow.SaaS.Orchestration;

public class FlowExecutionResult
{
    public bool Succeeded { get; set; }

    public string? Error { get; set; }

    public string VariablesJson { get; set; } = "{}";

    public string? OutputDataJson { get; set; }

    public List<string> VisibleFields { get; set; } = [];

    /// <summary>最终出参契约字段数（用于 omittedFieldCount）</summary>
    public int ContractFieldCount { get; set; }

    public List<NodeExecutionRecord> Nodes { get; set; } = [];

    public FlowRuntimeContext? Context { get; set; }

    /// <summary>解释器纯执行耗时（不含查库/落库）</summary>
    public int ExecuteMs { get; set; }
}

public class FlowExecutor : ITransientDependency
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly SystemContextBuilder _systemContextBuilder;
    private readonly IPermissionChecker _permissionChecker;
    private readonly IDataSourceResolver _dataSourceResolver;
    private readonly ICodeSandboxExecutor _codeSandboxExecutor;
    private readonly IOrchestrationRabbitPublisher _rabbitPublisher;
    private readonly PublishedFlowCache _publishedFlowCache;
    private readonly ILogger<FlowExecutor> _logger;

    public FlowExecutor(
        IHttpClientFactory httpClientFactory,
        SystemContextBuilder systemContextBuilder,
        IPermissionChecker permissionChecker,
        IDataSourceResolver dataSourceResolver,
        ICodeSandboxExecutor codeSandboxExecutor,
        IOrchestrationRabbitPublisher rabbitPublisher,
        PublishedFlowCache publishedFlowCache,
        ILogger<FlowExecutor> logger
    )
    {
        _httpClientFactory = httpClientFactory;
        _systemContextBuilder = systemContextBuilder;
        _permissionChecker = permissionChecker;
        _dataSourceResolver = dataSourceResolver;
        _codeSandboxExecutor = codeSandboxExecutor;
        _rabbitPublisher = rabbitPublisher;
        _publishedFlowCache = publishedFlowCache;
        _logger = logger;
    }

    public async Task<FlowExecutionResult> ExecuteAsync(
        string dslJson,
        string? requestBodyJson,
        bool isDryRun,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null,
        CancellationToken cancellationToken = default,
        bool skipValidation = false,
        HashSet<string>? callStack = null
    )
    {
        var swAll = Stopwatch.StartNew();
        var result = new FlowExecutionResult();
        FlowDslDocument dsl;
        try
        {
            dsl = JsonSerializer.Deserialize<FlowDslDocument>(dslJson, JsonOptions)
                  ?? throw new UserFriendlyException("DSL is empty.");
            if (!skipValidation)
            {
                ValidateDsl(dsl);
            }
        }
        catch (Exception ex) when (ex is not UserFriendlyException)
        {
            result.Succeeded = false;
            result.Error = ex.Message;
            result.ExecuteMs = (int)swAll.ElapsedMilliseconds;
            return result;
        }

        await ExecuteParsedCoreAsync(
            dsl,
            requestBodyJson,
            isDryRun,
            filterOutputsByRole,
            currentUser,
            result,
            callStack ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            cancellationToken
        );
        result.ExecuteMs = (int)swAll.ElapsedMilliseconds;
        return result;
    }

    /// <summary>使用已校验 DSL 快照；内部会再反序列化一份，避免并发共享 JsonNode。</summary>
    public Task<FlowExecutionResult> ExecuteParsedAsync(
        FlowDslDocument dsl,
        string? requestBodyJson,
        bool isDryRun,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null,
        CancellationToken cancellationToken = default
    )
    {
        // 走 JSON 拷贝，保证热路径缓存的文档不被执行过程改写
        var json = JsonSerializer.Serialize(dsl, JsonOptions);
        return ExecuteAsync(
            json,
            requestBodyJson,
            isDryRun,
            filterOutputsByRole,
            currentUser,
            cancellationToken,
            skipValidation: true
        );
    }

    private async Task ExecuteParsedCoreAsync(
        FlowDslDocument dsl,
        string? requestBodyJson,
        bool isDryRun,
        bool filterOutputsByRole,
        Volo.Abp.Users.ICurrentUser? currentUser,
        FlowExecutionResult result,
        HashSet<string> callStack,
        CancellationToken cancellationToken
    )
    {
        var flowKey = (dsl.Key ?? "").Trim();
        if (!string.IsNullOrEmpty(flowKey) && !callStack.Add(flowKey))
        {
            result.Succeeded = false;
            result.Error = $"SubFlow cycle detected involving '{flowKey}'.";
            return;
        }

        try
        {
            await ExecuteParsedCoreBodyAsync(
                dsl,
                requestBodyJson,
                isDryRun,
                filterOutputsByRole,
                currentUser,
                result,
                callStack,
                cancellationToken
            );
        }
        finally
        {
            if (!string.IsNullOrEmpty(flowKey))
            {
                callStack.Remove(flowKey);
            }
        }
    }

    private async Task ExecuteParsedCoreBodyAsync(
        FlowDslDocument dsl,
        string? requestBodyJson,
        bool isDryRun,
        bool filterOutputsByRole,
        Volo.Abp.Users.ICurrentUser? currentUser,
        FlowExecutionResult result,
        HashSet<string> callStack,
        CancellationToken cancellationToken
    )
    {
        var snapshot = _systemContextBuilder.Build();
        var ctx = BuildContext(dsl, requestBodyJson, snapshot);
        result.Context = ctx;

        var nodeMap = dsl.Nodes.ToDictionary(x => x.Id, StringComparer.OrdinalIgnoreCase);
        var outgoing = dsl.Edges
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var start = dsl.Nodes.First(x => string.Equals(x.Type, "Start", StringComparison.OrdinalIgnoreCase));
        var currentId = start.Id;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const int maxSteps = 200;

        try
        {
            for (var step = 0; step < maxSteps; step++)
            {
                if (!nodeMap.TryGetValue(currentId, out var node))
                {
                    throw new UserFriendlyException($"Node '{currentId}' not found.");
                }

                if (!visited.Add(currentId) &&
                    !string.Equals(node.Type, "End", StringComparison.OrdinalIgnoreCase))
                {
                    throw new UserFriendlyException($"Cycle detected at node '{currentId}'.");
                }

                var record = await ExecuteNodeAsync(node, ctx, isDryRun, callStack, cancellationToken);
                result.Nodes.Add(record);

                if (string.Equals(record.Status, "Failed", StringComparison.OrdinalIgnoreCase))
                {
                    result.Succeeded = false;
                    result.Error = record.Error;
                    result.VariablesJson = FlowContextResolver.SerializeContext(ctx);
                    return;
                }

                if (string.Equals(node.Type, "End", StringComparison.OrdinalIgnoreCase))
                {
                    await FinishOutputsAsync(dsl, node, ctx, result, filterOutputsByRole, currentUser, isDryRun);
                    result.Succeeded = true;
                    result.VariablesJson = FlowContextResolver.SerializeContext(ctx);
                    return;
                }

                if (!outgoing.TryGetValue(currentId, out var edges) || edges.Count == 0)
                {
                    throw new UserFriendlyException($"Node '{currentId}' has no outgoing edge.");
                }

                var nextId = ResolveNextNode(node, edges, ctx);
                if (string.IsNullOrWhiteSpace(nextId))
                {
                    throw new UserFriendlyException($"No matching branch from node '{currentId}'.");
                }

                currentId = nextId;
            }

            throw new UserFriendlyException("Exceeded maximum execution steps.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flow execution failed");
            result.Succeeded = false;
            result.Error = ex.Message;
            result.VariablesJson = FlowContextResolver.SerializeContext(ctx);
        }
    }

    private FlowRuntimeContext BuildContext(FlowDslDocument dsl, string? requestBodyJson, SystemContextSnapshot snapshot)
    {
        var ctx = new FlowRuntimeContext
        {
            Sys = (JsonObject)snapshot.Sys.DeepClone()!,
            Snapshot = snapshot
        };

        JsonObject? body = null;
        if (!string.IsNullOrWhiteSpace(requestBodyJson))
        {
            body = JsonNode.Parse(requestBodyJson) as JsonObject
                   ?? throw new UserFriendlyException("Request body must be a JSON object.");
        }

        // legacy: flat variablesJson without InputSchema
        if (dsl.Inputs.Count == 0 && body != null)
        {
            foreach (var (key, value) in body)
            {
                ctx.Vars[key] = value?.DeepClone();
                ctx.FlatVarsLegacy[key] = value?.DeepClone();
                ctx.Input[key] = value?.DeepClone();
            }

            return ctx;
        }

        foreach (var param in dsl.Inputs)
        {
            JsonNode? value = null;
            var source = param.Source ?? "input";

            if (source.Equals("system", StringComparison.OrdinalIgnoreCase))
            {
                value = _systemContextBuilder.ResolveSystemValue(param.SystemKey, param.SystemExpr, snapshot);
            }
            else if (param.Default != null)
            {
                value = param.Default.DeepClone();
            }

            if (source.Equals("input", StringComparison.OrdinalIgnoreCase) &&
                body != null &&
                body.TryGetPropertyValue(param.Name, out var incoming) &&
                incoming != null)
            {
                value = incoming.DeepClone();
            }

            if (value == null && (param.Required || HasRequiredRule(param)))
            {
                throw new UserFriendlyException(
                    FirstRuleMessage(param, "required") ?? $"Required input '{param.Name}' is missing."
                );
            }

            ctx.Input[param.Name] = value?.DeepClone();
            // seed vars for backward-compatible templates
            ctx.Vars[param.Name] = value?.DeepClone();
            ctx.FlatVarsLegacy[param.Name] = value?.DeepClone();
        }

        InputRuleValidator.ValidateInputs(dsl.Inputs, ctx);

        // ignore body keys that attempt to override system params
        return ctx;
    }

    private static bool HasRequiredRule(FlowInputParameterDsl param)
    {
        return param.Rules?.Any(r => string.Equals(r.Type, "required", StringComparison.OrdinalIgnoreCase)) == true;
    }

    private static string? FirstRuleMessage(FlowInputParameterDsl param, string type)
    {
        return param.Rules?
            .FirstOrDefault(r => string.Equals(r.Type, type, StringComparison.OrdinalIgnoreCase))
            ?.Message;
    }

    private async Task FinishOutputsAsync(
        FlowDslDocument dsl,
        FlowDslNode endNode,
        FlowRuntimeContext ctx,
        FlowExecutionResult result,
        bool filterOutputsByRole,
        Volo.Abp.Users.ICurrentUser? currentUser,
        bool isDryRun
    )
    {
        var schema = ResolveFinalSchema(dsl, endNode);
        if (schema.Count == 0)
        {
            result.OutputDataJson = ctx.Vars.ToJsonString(JsonOptions);
            result.VisibleFields = ctx.Vars.Select(x => x.Key).ToList();
            result.ContractFieldCount = result.VisibleFields.Count;
            return;
        }

        var bypass = isDryRun || !filterOutputsByRole || currentUser == null;
        var roles = new HashSet<string>(currentUser?.Roles ?? [], StringComparer.OrdinalIgnoreCase);
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!bypass)
        {
            foreach (var name in FinalOutputAssembler
                         .CollectPermissionNames(schema)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (await _permissionChecker.IsGrantedAsync(name))
                {
                    permissions.Add(name);
                }
            }
        }

        var assembled = FinalOutputAssembler.Assemble(schema, ctx, roles, permissions, bypass);
        result.OutputDataJson = assembled.Data.ToJsonString(JsonOptions);
        result.VisibleFields = assembled.VisibleFields;
        result.ContractFieldCount = assembled.ContractFieldCount;

        var sensitiveNames = schema.Where(x => x.Sensitive).Select(x => x.Name).ToList();
        if (sensitiveNames.Count > 0)
        {
            _logger.LogInformation(
                "Flow final output assembled. Visible={VisibleCount}, SensitiveFields={Sensitive}",
                assembled.VisibleFields.Count,
                string.Join(',', sensitiveNames)
            );
        }
    }

    private static List<FlowOutputParameterDsl> ResolveFinalSchema(FlowDslDocument dsl, FlowDslNode endNode)
    {
        if (endNode.FinalOutputs is { Count: > 0 })
        {
            return endNode.FinalOutputs;
        }

        return dsl.Outputs;
    }

    private static string? ResolveNextNode(FlowDslNode node, List<FlowDslEdge> edges, FlowRuntimeContext ctx)
    {
        if (!string.Equals(node.Type, "Condition", StringComparison.OrdinalIgnoreCase))
        {
            return edges.First().Target;
        }

        // New model: edge.combine / isDefault, shared node.Items
        var hasCombineModel = edges.Any(e => !string.IsNullOrWhiteSpace(e.Combine) || e.IsDefault) ||
                              (node.Items is { Count: > 0 });

        if (hasCombineModel && node.Items is { Count: > 0 })
        {
            var itemResults = EvaluateConditionItems(node.Items, ctx);
            var strategy = node.Strategy ?? "firstMatch";
            FlowDslEdge? matched = null;
            foreach (var edge in edges.Where(e => !e.IsDefault))
            {
                var combine = edge.Combine;
                if (string.IsNullOrWhiteSpace(combine) && edge.Items is { Count: > 0 })
                {
                    var local = EvaluateConditionItems(edge.Items, ctx);
                    combine = string.Join(" and ", local.Keys.OrderBy(x => x));
                    if (!ConditionCombineEvaluator.Evaluate(combine, local))
                    {
                        continue;
                    }

                    matched = edge;
                    break;
                }

                if (ConditionCombineEvaluator.Evaluate(combine, itemResults))
                {
                    matched = edge;
                    if (strategy.Equals("firstMatch", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }
            }

            if (matched != null)
            {
                return matched.Target;
            }

            return edges.FirstOrDefault(e => e.IsDefault)?.Target
                   ?? edges.FirstOrDefault(e => string.Equals(e.When, "false", StringComparison.OrdinalIgnoreCase))?.Target;
        }

        // Legacy: expression + when true/false
        var ok = EvaluateLegacyCondition(node.Expression, ctx);
        var branch = ok ? "true" : "false";
        return edges.FirstOrDefault(e => string.Equals(e.When, branch, StringComparison.OrdinalIgnoreCase))?.Target
               ?? edges.FirstOrDefault(e => string.IsNullOrWhiteSpace(e.When) && !e.IsDefault)?.Target
               ?? edges.FirstOrDefault(e => e.IsDefault)?.Target;
    }

    private async Task<NodeExecutionRecord> ExecuteNodeAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        bool isDryRun,
        HashSet<string> callStack,
        CancellationToken cancellationToken
    )
    {
        var sw = Stopwatch.StartNew();
        var record = new NodeExecutionRecord
        {
            NodeId = node.Id,
            NodeType = node.Type,
            Status = "Succeeded"
        };

        try
        {
            if (node.Entry != null &&
                (node.Entry.Items.Count > 0 || !string.IsNullOrWhiteSpace(node.Entry.Combine)))
            {
                var entryOk = EvaluateConditionBlock(node.Entry, ctx);
                if (!entryOk)
                {
                    record.Status = "Skipped";
                    record.OutputJson = JsonSerializer.Serialize(new { skipped = true, reason = "entry-condition" }, JsonOptions);
                    return record;
                }
            }

            var type = node.Type.ToLowerInvariant();
            if (type == "logic")
            {
                type = (node.Kind ?? "Http").ToLowerInvariant();
            }

            switch (type)
            {
                case "start":
                case "end":
                    record.OutputJson = "{}";
                    break;

                case "log":
                {
                    var nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
                    var template = nodeInput["message"]?.GetValue<string>()
                                   ?? node.Message
                                   ?? node.Expression
                                   ?? "";
                    var message = FlowContextResolver.ResolveTemplate(template, ctx);
                    var raw = new JsonObject
                    {
                        ["message"] = message,
                        ["logged"] = true
                    };
                    record.InputJson = JsonSerializer.Serialize(new { message, nodeInput }, JsonOptions);
                    ApplyExecutableOutputs(node, raw, ctx);
                    CheckFailWhen(node, ctx);
                    record.OutputJson = raw.ToJsonString(JsonOptions);
                    _logger.LogInformation("[FlowLog] {Message}", message);
                    break;
                }

                case "assign":
                case "setvariable":
                {
                    ExecuteAssignNode(node, ctx, record);
                    CheckFailWhen(node, ctx);
                    break;
                }

                case "mask":
                {
                    ExecuteMaskNode(node, ctx, record);
                    CheckFailWhen(node, ctx);
                    break;
                }

                case "throw":
                case "assert":
                {
                    ExecuteThrowNode(node, ctx, record);
                    break;
                }

                case "condition":
                {
                    object payload;
                    if (node.Items is { Count: > 0 })
                    {
                        var items = EvaluateConditionItems(node.Items, ctx);
                        payload = new { items, strategy = node.Strategy ?? "firstMatch" };
                    }
                    else
                    {
                        var ok = EvaluateLegacyCondition(node.Expression, ctx);
                        payload = new { expression = node.Expression, result = ok };
                    }

                    record.InputJson = JsonSerializer.Serialize(payload, JsonOptions);
                    record.OutputJson = record.InputJson;
                    break;
                }

                case "http":
                case "httpcall":
                {
                    if (isDryRun)
                    {
                        // 试运行：异步按同步语义，但 Http 仍跳过真实外呼（可用 mock:// 测完整链路）
                        record.Status = "Skipped";
                        record.OutputJson = JsonSerializer.Serialize(
                            new { skipped = true, reason = "dry-run", async = node.Async },
                            JsonOptions
                        );
                        break;
                    }

                    if (node.Async)
                    {
                        await QueueHttpCallAsync(node, ctx, record, cancellationToken);
                        break;
                    }

                    await ExecuteHttpAsync(node, ctx, record, cancellationToken);
                    CheckFailWhen(node, ctx);
                    break;
                }

                case "code":
                {
                    await ExecuteCodeNodeAsync(node, ctx, record, isDryRun, cancellationToken);
                    CheckFailWhen(node, ctx);
                    break;
                }

                case "rabbitmqpublish":
                case "rabbitpublish":
                case "broadcast":
                {
                    await ExecuteRabbitMqPublishAsync(node, ctx, record, isDryRun, cancellationToken);
                    CheckFailWhen(node, ctx);
                    break;
                }

                case "subflow":
                case "logiccomponent":
                case "component":
                {
                    await ExecuteSubFlowAsync(node, ctx, record, isDryRun, callStack, cancellationToken);
                    CheckFailWhen(node, ctx);
                    break;
                }

                default:
                    throw new UserFriendlyException($"Unsupported node type '{node.Type}'.");
            }
        }
        catch (Exception ex)
        {
            record.Status = "Failed";
            record.Error = ex.Message;
        }
        finally
        {
            sw.Stop();
            record.DurationMs = (int)sw.ElapsedMilliseconds;
        }

        return record;
    }

    /// <summary>
    /// 异步 Http：主链只排队，不写出参；后台发请求（副作用），失败仅记日志。
    /// </summary>
    private Task QueueHttpCallAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        CancellationToken cancellationToken
    )
    {
        var methodText = string.IsNullOrWhiteSpace(node.Method) ? "GET" : node.Method.Trim();
        var url = FlowContextResolver.ResolveTemplate(node.Url ?? "", ctx);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new UserFriendlyException("Http requires url.");
        }

        var method = new HttpMethod(methodText.ToUpperInvariant());
        var nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        nodeInput.Remove("method");
        nodeInput.Remove("url");

        var bodyMode = (node.BodyMode ?? "auto").Trim().ToLowerInvariant();
        if (bodyMode is "auto")
        {
            bodyMode = method == HttpMethod.Get || method == HttpMethod.Delete ? "query" : "json";
        }

        string? bodyText = null;
        var finalUrl = url;
        switch (bodyMode)
        {
            case "query":
                finalUrl = AppendQueryString(url, nodeInput);
                break;
            case "json":
                if (nodeInput.Count > 0)
                {
                    bodyText = nodeInput.ToJsonString(JsonOptions);
                }
                else if (node.Body != null)
                {
                    var resolved = ResolveJsonTemplates(node.Body.DeepClone()!, ctx);
                    bodyText = resolved is JsonValue jv && jv.TryGetValue<string>(out var s)
                        ? FlowContextResolver.ResolveTemplate(s, ctx)
                        : resolved.ToJsonString(JsonOptions);
                }
                break;
            case "form":
                bodyText = BuildFormBody(nodeInput);
                break;
            case "raw":
                if (node.Body != null)
                {
                    var resolved = ResolveJsonTemplates(node.Body.DeepClone()!, ctx);
                    bodyText = resolved is JsonValue jv && jv.TryGetValue<string>(out var s)
                        ? FlowContextResolver.ResolveTemplate(s, ctx)
                        : resolved.ToJsonString(JsonOptions);
                }
                break;
            default:
                throw new UserFriendlyException($"Unsupported HTTP bodyMode '{node.BodyMode}'.");
        }

        var headers = FlowContextResolver.MapNodeInputs(node.Headers, ctx);
        var headerPairs = new List<KeyValuePair<string, string>>();
        foreach (var (k, v) in headers)
        {
            if (string.IsNullOrWhiteSpace(k) || v == null)
            {
                continue;
            }

            if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var headerValue = v.GetValueKind() == System.Text.Json.JsonValueKind.String
                ? v.GetValue<string>() ?? ""
                : v.ToJsonString(JsonOptions).Trim('"');
            headerPairs.Add(new KeyValuePair<string, string>(k, headerValue));
        }

        record.Status = "AsyncQueued";
        record.InputJson = JsonSerializer.Serialize(
            new { async = true, method = method.Method, url = finalUrl, bodyMode, headers, parameters = nodeInput, body = bodyText },
            JsonOptions
        );
        record.OutputJson = JsonSerializer.Serialize(
            new
            {
                async = true,
                queued = true,
                message = "HttpCall queued; outputs are not available on the main chain until Wait (P2)."
            },
            JsonOptions
        );

        // mock:// 无需后台外呼
        if (finalUrl.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Async HttpCall {NodeId} mock skipped on queue: {Url}", node.Id, finalUrl);
            return Task.CompletedTask;
        }

        var clientFactory = _httpClientFactory;
        var logger = _logger;
        var nodeId = node.Id;
        var methodCopy = method;
        var urlCopy = finalUrl;
        var bodyCopy = bodyText;
        var bodyModeCopy = bodyMode;
        var headersCopy = headerPairs;

        _ = Task.Run(async () =>
        {
            try
            {
                using var request = new HttpRequestMessage(methodCopy, urlCopy);
                foreach (var (k, v) in headersCopy)
                {
                    request.Headers.TryAddWithoutValidation(k, v);
                }

                if (bodyCopy != null && methodCopy != HttpMethod.Get)
                {
                    var contentType = bodyModeCopy == "form"
                        ? "application/x-www-form-urlencoded"
                        : "application/json";
                    request.Content = new StringContent(bodyCopy, Encoding.UTF8, contentType);
                }

                var client = clientFactory.CreateClient("OrchestrationHttpCall");
                using var response = await client.SendAsync(request);
                logger.LogInformation(
                    "Async HttpCall {NodeId} completed status={Status} url={Url}",
                    nodeId,
                    (int)response.StatusCode,
                    urlCopy
                );
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Async HttpCall {NodeId} failed url={Url}", nodeId, urlCopy);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }

    private async Task ExecuteHttpAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        CancellationToken cancellationToken
    )
    {
        // method / url 仅来自节点配置，不再被 inputs 覆盖
        var methodText = string.IsNullOrWhiteSpace(node.Method) ? "GET" : node.Method.Trim();
        var url = FlowContextResolver.ResolveTemplate(node.Url ?? "", ctx);
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new UserFriendlyException("Http requires url.");
        }

        var method = new HttpMethod(methodText.ToUpperInvariant());
        var nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        // 兼容旧 DSL：inputs 里误放的 method/url 忽略
        nodeInput.Remove("method");
        nodeInput.Remove("url");

        var bodyMode = (node.BodyMode ?? "auto").Trim().ToLowerInvariant();
        if (bodyMode is "auto")
        {
            bodyMode = method == HttpMethod.Get || method == HttpMethod.Delete ? "query" : "json";
        }

        string? bodyText = null;
        var finalUrl = url;

        switch (bodyMode)
        {
            case "query":
                finalUrl = AppendQueryString(url, nodeInput);
                break;
            case "json":
                if (nodeInput.Count > 0)
                {
                    bodyText = nodeInput.ToJsonString(JsonOptions);
                }
                else if (node.Body != null)
                {
                    var resolved = ResolveJsonTemplates(node.Body.DeepClone()!, ctx);
                    bodyText = resolved is JsonValue jv && jv.TryGetValue<string>(out var s)
                        ? FlowContextResolver.ResolveTemplate(s, ctx)
                        : resolved.ToJsonString(JsonOptions);
                }
                break;
            case "form":
                bodyText = BuildFormBody(nodeInput);
                break;
            case "raw":
                if (node.Body != null)
                {
                    var resolved = ResolveJsonTemplates(node.Body.DeepClone()!, ctx);
                    bodyText = resolved is JsonValue jv && jv.TryGetValue<string>(out var s)
                        ? FlowContextResolver.ResolveTemplate(s, ctx)
                        : resolved.ToJsonString(JsonOptions);
                }
                break;
            default:
                throw new UserFriendlyException($"Unsupported HTTP bodyMode '{node.BodyMode}'.");
        }

        var headers = FlowContextResolver.MapNodeInputs(node.Headers, ctx);

        record.InputJson = JsonSerializer.Serialize(
            new { method = method.Method, url = finalUrl, bodyMode, headers, parameters = nodeInput, body = bodyText },
            JsonOptions
        );

        // 本地 mock：演示/联调不走外网（如 mock://echo），避免 httpbin 等 2s+ 延迟
        if (TryBuildMockHttpResponse(finalUrl, method.Method, headers, bodyText, out var mockStatus, out var mockBody, out var mockRawText))
        {
            FinishHttpCall(node, ctx, record, mockStatus, mockBody, mockRawText);
            return;
        }

        using var request = new HttpRequestMessage(method, finalUrl);
        foreach (var (k, v) in headers)
        {
            if (string.IsNullOrWhiteSpace(k) || v == null)
            {
                continue;
            }

            var headerValue = v.GetValueKind() == System.Text.Json.JsonValueKind.String
                ? v.GetValue<string>()
                : v.ToJsonString(JsonOptions).Trim('"');
            if (string.Equals(k, "Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                continue; // content-type 由 body 模式决定
            }

            request.Headers.TryAddWithoutValidation(k, headerValue);
        }

        if (bodyText != null && method != HttpMethod.Get)
        {
            var contentType = bodyMode == "form"
                ? "application/x-www-form-urlencoded"
                : "application/json";
            request.Content = new StringContent(bodyText, Encoding.UTF8, contentType);
        }

        var client = _httpClientFactory.CreateClient("OrchestrationHttpCall");
        using var response = await client.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        JsonNode? bodyParsed;
        try
        {
            bodyParsed = JsonNode.Parse(responseText);
        }
        catch
        {
            bodyParsed = JsonValue.Create(responseText);
        }

        FinishHttpCall(node, ctx, record, (int)response.StatusCode, bodyParsed, responseText, throwOnErrorStatus: true);
    }

    /// <summary>
    /// mock://echo | mock://httpbin/post — 即时回声，形状兼容 httpbin POST。
    /// mock://status/{code} — 返回指定状态码（空 body）。
    /// </summary>
    private static bool TryBuildMockHttpResponse(
        string url,
        string method,
        JsonObject headers,
        string? bodyText,
        out int statusCode,
        out JsonNode? body,
        out string rawText
    )
    {
        statusCode = 200;
        body = null;
        rawText = "";

        if (!url.StartsWith("mock://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var path = url["mock://".Length..].Trim().TrimStart('/');
        if (path.StartsWith("status/", StringComparison.OrdinalIgnoreCase))
        {
            var codeText = path["status/".Length..].Split('?', 2)[0];
            if (!int.TryParse(codeText, out statusCode))
            {
                statusCode = 500;
            }

            body = new JsonObject { ["url"] = url, ["status"] = statusCode };
            rawText = body.ToJsonString(JsonOptions);
            return true;
        }

        // echo / httpbin/post / 默认
        JsonNode? jsonPayload = null;
        if (!string.IsNullOrWhiteSpace(bodyText))
        {
            try
            {
                jsonPayload = JsonNode.Parse(bodyText);
            }
            catch
            {
                jsonPayload = JsonValue.Create(bodyText);
            }
        }

        var headerObj = new JsonObject();
        foreach (var (k, v) in headers)
        {
            if (string.IsNullOrWhiteSpace(k) || v == null)
            {
                continue;
            }

            headerObj[k] =
                v.GetValueKind() == System.Text.Json.JsonValueKind.String
                    ? v.GetValue<string>()
                    : v.DeepClone();
        }

        body = new JsonObject
        {
            ["url"] = url,
            ["method"] = method,
            ["headers"] = headerObj,
            ["json"] = jsonPayload?.DeepClone(),
            ["data"] = bodyText,
            ["origin"] = "mock"
        };
        rawText = body.ToJsonString(JsonOptions);
        statusCode = 200;
        return true;
    }

    private void FinishHttpCall(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        int statusCode,
        JsonNode? bodyParsed,
        string responseText,
        bool throwOnErrorStatus = false
    )
    {
        var raw = new JsonObject
        {
            ["status"] = statusCode,
            ["statusCode"] = statusCode,
            ["body"] = bodyParsed?.DeepClone(),
            ["response"] = new JsonObject
            {
                ["status"] = statusCode,
                ["body"] = bodyParsed?.DeepClone()
            }
        };

        record.OutputJson = JsonSerializer.Serialize(
            new { statusCode, body = Truncate(responseText, 4000) },
            JsonOptions
        );

        if (throwOnErrorStatus && statusCode is < 200 or >= 300)
        {
            throw new UserFriendlyException($"HttpCall failed with {statusCode}.");
        }

        ApplyExecutableOutputs(node, raw, ctx);
        if (!string.IsNullOrWhiteSpace(node.ResponseVariable))
        {
            ctx.Vars[node.ResponseVariable!] = bodyParsed?.DeepClone();
            ctx.FlatVarsLegacy[node.ResponseVariable!] = bodyParsed?.DeepClone();
        }
    }

    private static string AppendQueryString(string url, JsonObject parameters)
    {
        if (parameters.Count == 0)
        {
            return url;
        }

        var parts = new List<string>();
        foreach (var (k, v) in parameters)
        {
            if (string.IsNullOrWhiteSpace(k) || v == null || v.GetValueKind() == System.Text.Json.JsonValueKind.Null)
            {
                continue;
            }

            var value = v.GetValueKind() switch
            {
                System.Text.Json.JsonValueKind.String => v.GetValue<string>() ?? "",
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                System.Text.Json.JsonValueKind.Number => v.ToJsonString(JsonOptions),
                _ => v.ToJsonString(JsonOptions)
            };
            parts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(value)}");
        }

        if (parts.Count == 0)
        {
            return url;
        }

        var sep = url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return url + sep + string.Join("&", parts);
    }

    private static string BuildFormBody(JsonObject parameters)
    {
        var parts = new List<string>();
        foreach (var (k, v) in parameters)
        {
            if (string.IsNullOrWhiteSpace(k) || v == null)
            {
                continue;
            }

            var value = v.GetValueKind() == System.Text.Json.JsonValueKind.String
                ? v.GetValue<string>() ?? ""
                : v.ToJsonString(JsonOptions).Trim('"');
            parts.Add($"{Uri.EscapeDataString(k)}={Uri.EscapeDataString(value)}");
        }

        return string.Join("&", parts);
    }

    /// <summary>条件成立则抛出业务异常，试运行/正式执行都会在实例错误中展示。</summary>
    private static void CheckFailWhen(FlowDslNode node, FlowRuntimeContext ctx)
    {
        if (!EvaluateFailPredicate(node, ctx))
        {
            return;
        }

        ThrowBusinessFail(node, ctx);
    }

    /// <summary>
    /// 优先 FailItems + FailCombine（1 and (2 or 3)）；否则 FailWhen 布尔表达式。
    /// 可引用本节点已发布出参短名、input.*、sys.*、引用名.出参。
    /// </summary>
    private static bool EvaluateFailPredicate(FlowDslNode node, FlowRuntimeContext ctx)
    {
        if (node.FailItems is { Count: > 0 })
        {
            var itemResults = EvaluateConditionItems(node.FailItems, ctx);
            var combine = node.FailCombine;
            if (string.IsNullOrWhiteSpace(combine))
            {
                combine = string.Join(" and ", itemResults.Keys.OrderBy(x => x));
            }

            return ConditionCombineEvaluator.Evaluate(combine, itemResults);
        }

        var when = node.FailWhen ?? node.Expression;
        if (string.IsNullOrWhiteSpace(when))
        {
            return false;
        }

        return BoolExpressionEvaluator.Evaluate(when, ctx);
    }

    private static void ThrowBusinessFail(FlowDslNode node, FlowRuntimeContext ctx)
    {
        var message = FlowContextResolver.ResolveTemplate(
            string.IsNullOrWhiteSpace(node.FailMessage)
                ? (node.Message ?? "业务规则校验失败")
                : node.FailMessage!,
            ctx
        );
        if (!string.IsNullOrWhiteSpace(node.FailCode))
        {
            message = $"[{node.FailCode}] {message}";
        }

        throw new UserFriendlyException(message);
    }

    private static void ExecuteThrowNode(FlowDslNode node, FlowRuntimeContext ctx, NodeExecutionRecord record)
    {
        // 无条件时默认抛出
        var hasPredicate =
            (node.FailItems is { Count: > 0 }) ||
            !string.IsNullOrWhiteSpace(node.FailWhen) ||
            !string.IsNullOrWhiteSpace(node.Expression);

        var shouldThrow = !hasPredicate || EvaluateFailPredicate(node, ctx);
        record.InputJson = JsonSerializer.Serialize(
            new
            {
                failItems = node.FailItems,
                failCombine = node.FailCombine,
                failWhen = node.FailWhen,
                shouldThrow
            },
            JsonOptions
        );

        if (!shouldThrow)
        {
            record.OutputJson = JsonSerializer.Serialize(new { thrown = false }, JsonOptions);
            ApplyExecutableOutputs(
                node,
                new JsonObject { ["thrown"] = false },
                ctx
            );
            return;
        }

        record.OutputJson = JsonSerializer.Serialize(new { thrown = true }, JsonOptions);
        ThrowBusinessFail(node, ctx);
    }

    /// <summary>
    /// 赋值节点：像方法调用一样，inputs 映射后写入 results[nodeId].*（outputs 声明契约）。
    /// 兼容旧 SetVariable 的 name/value。
    /// </summary>
    private static void ExecuteAssignNode(FlowDslNode node, FlowRuntimeContext ctx, NodeExecutionRecord record)
    {
        JsonObject nodeInput;
        if (node.Inputs is { Count: > 0 })
        {
            nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        }
        else
        {
            // legacy SetVariable
            var name = node.Name ?? node.Variable;
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new UserFriendlyException("Assign requires inputs[] or legacy name.");
            }

            var valueNode = ResolveValueNode(node.Value, ctx);
            nodeInput = new JsonObject { [name] = valueNode };
        }

        record.InputJson = nodeInput.ToJsonString(JsonOptions);
        ApplyExecutableOutputs(node, nodeInput, ctx);
        record.OutputJson = nodeInput.ToJsonString(JsonOptions);
    }

    private static void ExecuteMaskNode(FlowDslNode node, FlowRuntimeContext ctx, NodeExecutionRecord record)
    {
        var nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        var strategy = (node.MaskStrategy ?? node.Strategy ?? "rules").Trim();
        JsonObject raw;

        if (strategy.Equals("items", StringComparison.OrdinalIgnoreCase))
        {
            var field = string.IsNullOrWhiteSpace(node.ItemField) ? "rows" : node.ItemField.Trim();
            if (nodeInput[field] is not JsonArray rows)
            {
                throw new UserFriendlyException($"Mask items strategy requires array input '{field}'.");
            }

            var maskedRows = MaskValueHelper.ApplyItemRules(rows, node.MaskRules, ctx);
            raw = new JsonObject { [field] = maskedRows };
            // 同步其它非 list 入参
            foreach (var kv in nodeInput)
            {
                if (!string.Equals(kv.Key, field, StringComparison.OrdinalIgnoreCase))
                {
                    raw[kv.Key] = kv.Value?.DeepClone();
                }
            }
        }
        else
        {
            raw = MaskValueHelper.ApplyObjectRules(nodeInput, node.MaskRules, ctx);
        }

        record.InputJson = MaskValueHelper.RedactForLog(nodeInput, node.MaskRules).ToJsonString(JsonOptions);
        ApplyExecutableOutputs(node, raw, ctx);
        record.OutputJson = raw.ToJsonString(JsonOptions);
    }

    /// <summary>
    /// 可执行节点统一落库：results[nodeId] + 按 outputs 投影；无 outputs 时每个顶层字段即出参名。
    /// </summary>
    private static void ApplyExecutableOutputs(FlowDslNode node, JsonNode? raw, FlowRuntimeContext ctx)
    {
        var bindings = node.Outputs;
        if (bindings == null || bindings.Count == 0)
        {
            if (raw is JsonObject obj)
            {
                bindings = obj
                    .Select(kv => new FlowNodeBindingDsl
                    {
                        Name = kv.Key,
                        From = JsonValue.Create(kv.Key)
                    })
                    .ToList();
            }
            else
            {
                bindings =
                [
                    new FlowNodeBindingDsl
                    {
                        Name = "value",
                        From = JsonValue.Create("")
                    }
                ];
                raw = new JsonObject { ["value"] = raw?.DeepClone() };
            }
        }

        FlowContextResolver.ApplyNodeOutputs(bindings, raw, node.Id, ctx, ResolveNodeRef(node), node.ResultRoot);
    }

    private static string ResolveNodeRef(FlowDslNode node)
    {
        if (!string.IsNullOrWhiteSpace(node.Ref))
        {
            return node.Ref.Trim();
        }

        return node.Id;
    }

    private async Task ExecuteSubFlowAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        bool isDryRun,
        HashSet<string> callStack,
        CancellationToken cancellationToken
    )
    {
        var subKey = (node.SubFlowKey ?? node.Url ?? "").Trim();
        if (string.IsNullOrWhiteSpace(subKey))
        {
            throw new UserFriendlyException("SubFlow requires subFlowKey (published flow code).");
        }

        if (callStack.Count >= OrchestrationConsts.MaxSubFlowDepth)
        {
            throw new UserFriendlyException(
                $"SubFlow nesting exceeds limit ({OrchestrationConsts.MaxSubFlowDepth})."
            );
        }

        if (callStack.Contains(subKey))
        {
            throw new UserFriendlyException(
                $"SubFlow cycle detected: {string.Join(" → ", callStack)} → {subKey}"
            );
        }

        var nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        var bodyJson = nodeInput.ToJsonString(JsonOptions);
        var onError = (node.OnError ?? "fail").Trim().ToLowerInvariant();

        record.InputJson = JsonSerializer.Serialize(
            new
            {
                subFlowKey = subKey,
                dryRun = isDryRun,
                input = nodeInput
            },
            JsonOptions
        );

        var snap = await _publishedFlowCache.GetByKeyAsync(subKey);
        var child = await ExecuteAsync(
            snap.DslJson,
            bodyJson,
            isDryRun,
            filterOutputsByRole: false,
            currentUser: null,
            cancellationToken,
            skipValidation: true,
            callStack
        );

        if (!child.Succeeded)
        {
            if (onError != "ignore")
            {
                throw new UserFriendlyException(
                    child.Error ?? $"SubFlow '{subKey}' failed."
                );
            }

            _logger.LogWarning(
                "SubFlow ignored failure. Key={Key} Error={Error}",
                subKey,
                child.Error
            );
        }

        JsonNode? dataNode = null;
        if (!string.IsNullOrWhiteSpace(child.OutputDataJson))
        {
            try
            {
                dataNode = JsonNode.Parse(child.OutputDataJson);
            }
            catch
            {
                dataNode = JsonValue.Create(child.OutputDataJson);
            }
        }

        var raw = new JsonObject
        {
            ["success"] = child.Succeeded,
            ["subFlowKey"] = subKey,
            ["subFlowVersion"] = snap.Version,
            ["executeMs"] = child.ExecuteMs,
            ["data"] = dataNode?.DeepClone() ?? new JsonObject()
        };
        if (!string.IsNullOrWhiteSpace(child.Error))
        {
            raw["error"] = child.Error;
        }

        // 默认业务根 = data（子流程 End 出参）；outputs.from 相对 data
        if (string.IsNullOrWhiteSpace(node.ResultRoot))
        {
            node.ResultRoot = "data";
        }

        ApplyExecutableOutputs(node, raw, ctx);
        record.OutputJson = raw.ToJsonString(JsonOptions);
    }

    private async Task ExecuteRabbitMqPublishAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        var exchangeType = string.IsNullOrWhiteSpace(node.ExchangeType)
            ? OrchestrationRabbitConsts.DefaultExchangeType
            : node.ExchangeType.Trim();
        var exchange = string.IsNullOrWhiteSpace(node.Exchange)
            ? (exchangeType.Equals("fanout", StringComparison.OrdinalIgnoreCase)
                ? OrchestrationRabbitConsts.BroadcastExchange
                : OrchestrationRabbitConsts.TopicExchange)
            : node.Exchange.Trim();

        var routingKeyTemplate = node.RoutingKey ?? string.Empty;
        var routingKey = ResolveTemplateWithNodeInput(routingKeyTemplate, nodeInput, ctx);

        var payloadNode = BuildRabbitPayload(node, nodeInput, ctx);
        var jsonBody = payloadNode?.ToJsonString(JsonOptions) ?? "{}";
        var persistent = node.Persistent ?? true;
        var onError = (node.OnError ?? "fail").Trim().ToLowerInvariant();

        record.InputJson = JsonSerializer.Serialize(
            new
            {
                exchange,
                exchangeType,
                routingKey,
                persistent,
                dryRun = isDryRun,
                payload = payloadNode
            },
            JsonOptions
        );

        var published = false;
        string? publishError = null;

        if (!isDryRun)
        {
            try
            {
                await _rabbitPublisher.PublishAsync(
                    exchange,
                    exchangeType,
                    routingKey,
                    jsonBody,
                    persistent,
                    cancellationToken
                );
                published = true;
            }
            catch (Exception ex)
            {
                publishError = ex.Message;
                if (onError != "ignore")
                {
                    throw;
                }

                _logger.LogWarning(
                    ex,
                    "RabbitMqPublish ignored failure. Exchange={Exchange}",
                    exchange
                );
            }
        }

        var raw = new JsonObject
        {
            ["published"] = published,
            ["dryRun"] = isDryRun,
            ["exchange"] = exchange,
            ["exchangeType"] = exchangeType,
            ["routingKey"] = routingKey,
            ["persistent"] = persistent,
            ["payloadBytes"] = Encoding.UTF8.GetByteCount(jsonBody)
        };
        if (!string.IsNullOrWhiteSpace(publishError))
        {
            raw["error"] = publishError;
        }

        // 消息体由 payload 决定；出参表可空。空时只自动回执 published/exchange/routingKey，避免把 dryRun 等噪声写成短名。
        if (node.Outputs is { Count: > 0 })
        {
            ApplyExecutableOutputs(node, raw, ctx);
        }
        else
        {
            var receiptBindings = new List<FlowNodeBindingDsl>
            {
                new() { Name = "published", From = JsonValue.Create("published") },
                new() { Name = "exchange", From = JsonValue.Create("exchange") },
                new() { Name = "routingKey", From = JsonValue.Create("routingKey") }
            };
            FlowContextResolver.ApplyNodeOutputs(
                receiptBindings,
                raw,
                node.Id,
                ctx,
                ResolveNodeRef(node),
                node.ResultRoot
            );
        }

        record.OutputJson = raw.ToJsonString(JsonOptions);
    }

    private static string ResolveTemplateWithNodeInput(
        string template,
        JsonObject nodeInput,
        FlowRuntimeContext ctx
    )
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        if (!template.Contains("{{", StringComparison.Ordinal))
        {
            return template;
        }

        return System.Text.RegularExpressions.Regex.Replace(
            template,
            @"\{\{\s*([^}]+?)\s*\}\}",
            m =>
            {
                var path = m.Groups[1].Value.Trim();
                if (path.StartsWith("input.", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("sys.", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("vars.", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("results.", StringComparison.OrdinalIgnoreCase))
                {
                    return FlowContextResolver.ResolvePath(path, ctx)?.ToString() ?? "";
                }

                var local = FlowContextResolver.GetByPath(nodeInput, path);
                if (local != null)
                {
                    return FlowContextResolver.ToClr(local)?.ToString() ?? "";
                }

                return FlowContextResolver.ResolvePath(path, ctx)?.ToString() ?? "";
            }
        );
    }

    private static JsonNode? BuildRabbitPayload(
        FlowDslNode node,
        JsonObject nodeInput,
        FlowRuntimeContext ctx
    )
    {
        var mode = (node.PayloadMode ?? "object").Trim().ToLowerInvariant();
        if (mode == "raw")
        {
            var from = string.IsNullOrWhiteSpace(node.PayloadFrom) ? null : node.PayloadFrom.Trim();
            if (string.IsNullOrWhiteSpace(from))
            {
                return nodeInput.DeepClone();
            }

            return FinalOutputAssembler.ResolveRelative(JsonValue.Create(from), nodeInput, ctx)
                   ?? FlowContextResolver.ResolvePathNode(from, ctx)?.DeepClone();
        }

        if (node.Payload?.Item is { Count: > 0 })
        {
            // 消息体字段表 = End 出参同语义的「对象投影」，禁止走 ProjectWithMap 的 array 默认分支
            return FinalOutputAssembler.ProjectObjectFields(node.Payload.Item, nodeInput, ctx);
        }

        // 未配 payload：默认把本节点 inputs 整包发出（广播调试友好）
        return nodeInput.DeepClone();
    }

    private async Task ExecuteCodeNodeAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        record.InputJson = nodeInput.ToJsonString(JsonOptions);

        var script = node.Script?.Trim();
        JsonNode? returned;

        if (string.IsNullOrWhiteSpace(script) && node.Value != null)
        {
            returned = ResolveValueNode(node.Value, ctx);
        }
        else if (!string.IsNullOrWhiteSpace(script))
        {
            DataSource? dataSource = null;
            var dsId = node.ResolveDataSourceId();
            if (!string.IsNullOrWhiteSpace(dsId))
            {
                dataSource = await _dataSourceResolver.ResolveEnabledAsync(dsId, cancellationToken);
            }

            var canWrite = await _permissionChecker.IsGrantedAsync(Permissions.OrchestrationPermissions.Sql.Write);
            returned = await _codeSandboxExecutor.ExecuteAsync(
                new CodeSandboxRequest
                {
                    Script = script!,
                    NodeInput = nodeInput,
                    Input = ctx.Input,
                    Sys = ctx.Sys,
                    Results = ctx.Results,
                    DataSource = dataSource,
                    IsDryRun = isDryRun,
                    CanWriteSql = canWrite
                },
                cancellationToken
            );
        }
        else
        {
            returned = nodeInput.DeepClone();
        }

        var raw = new JsonObject { ["return"] = returned?.DeepClone() };
        if (returned is JsonObject obj)
        {
            foreach (var (k, v) in obj)
            {
                raw[k] = v?.DeepClone();
            }
        }

        ApplyExecutableOutputs(node, raw, ctx);
        record.OutputJson = raw.ToJsonString(JsonOptions);
    }

    private static bool EvaluateConditionBlock(FlowConditionBlockDsl block, FlowRuntimeContext ctx)
    {
        if (block.Items.Count == 0)
        {
            return true;
        }

        var results = EvaluateConditionItems(block.Items, ctx);
        return ConditionCombineEvaluator.Evaluate(block.Combine, results);
    }

    private static Dictionary<int, bool> EvaluateConditionItems(
        List<FlowConditionItemDsl> items,
        FlowRuntimeContext ctx
    )
    {
        var dict = new Dictionary<int, bool>();
        foreach (var item in items.OrderBy(x => x.No))
        {
            dict[item.No] = EvaluateConditionItem(item, ctx);
        }

        return dict;
    }

    private static bool EvaluateConditionItem(FlowConditionItemDsl item, FlowRuntimeContext ctx)
    {
        var left = FlowContextResolver.ResolvePath(item.Left, ctx);
        var right = item.Right is JsonValue jv && jv.TryGetValue<string>(out var rs)
            ? FlowContextResolver.ResolvePath(rs, ctx)
            : FlowContextResolver.ToClr(item.Right);

        var op = (item.Op ?? "eq").ToLowerInvariant();
        return op switch
        {
            "eq" or "==" => Compare(left, right, "=="),
            "ne" or "!=" => Compare(left, right, "!="),
            "gt" or ">" => Compare(left, right, ">"),
            "gte" or ">=" => Compare(left, right, ">="),
            "lt" or "<" => Compare(left, right, "<"),
            "lte" or "<=" => Compare(left, right, "<="),
            "contains" => (Convert.ToString(left) ?? "").Contains(Convert.ToString(right) ?? "", StringComparison.OrdinalIgnoreCase),
            "notcontains" => !(Convert.ToString(left) ?? "").Contains(Convert.ToString(right) ?? "", StringComparison.OrdinalIgnoreCase),
            "isempty" => left == null || string.IsNullOrWhiteSpace(Convert.ToString(left)),
            "isnotempty" => left != null && !string.IsNullOrWhiteSpace(Convert.ToString(left)),
            _ => throw new UserFriendlyException($"Unsupported condition op '{item.Op}'.")
        };
    }

    private static bool EvaluateLegacyCondition(string? expression, FlowRuntimeContext ctx)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return false;
        }

        var expr = expression.Trim();
        var match = System.Text.RegularExpressions.Regex.Match(
            expr,
            @"^(?<left>.+?)\s*(?<op>==|!=|>=|<=|>|<)\s*(?<right>.+)$"
        );
        if (match.Success)
        {
            var left = FlowContextResolver.ResolvePath(match.Groups["left"].Value.Trim(), ctx);
            var right = FlowContextResolver.ResolvePath(match.Groups["right"].Value.Trim(), ctx);
            return Compare(left, right, match.Groups["op"].Value);
        }

        return IsTruthy(FlowContextResolver.ResolvePath(expr, ctx));
    }

    private static JsonNode? ResolveValueNode(JsonNode? value, FlowRuntimeContext ctx)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonValue jv && jv.TryGetValue<string>(out var s))
        {
            if (s.Contains("{{"))
            {
                var resolved = FlowContextResolver.ResolveTemplate(s, ctx);
                return FlowContextResolver.ToJsonNode(resolved);
            }

            var pathVal = FlowContextResolver.ResolvePath(s, ctx);
            return FlowContextResolver.ToJsonNode(pathVal);
        }

        return ResolveJsonTemplates(value.DeepClone()!, ctx);
    }

    private static JsonNode ResolveJsonTemplates(JsonNode node, FlowRuntimeContext ctx)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(x => x.Key).ToList())
                {
                    if (obj[key] != null)
                    {
                        obj[key] = ResolveJsonTemplates(obj[key]!, ctx);
                    }
                }

                return obj;
            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    if (arr[i] != null)
                    {
                        arr[i] = ResolveJsonTemplates(arr[i]!, ctx);
                    }
                }

                return arr;
            case JsonValue value when value.TryGetValue<string>(out var s):
                return JsonValue.Create(FlowContextResolver.ResolveTemplate(s, ctx));
            default:
                return node;
        }
    }

    public static void ValidateDsl(FlowDslDocument dsl)
    {
        if (dsl.Nodes == null || dsl.Nodes.Count == 0)
        {
            throw new UserFriendlyException("DSL nodes are required.");
        }

        if (dsl.Nodes.Count(x => string.Equals(x.Type, "Start", StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new UserFriendlyException("DSL must contain exactly one Start node.");
        }

        if (dsl.Nodes.All(x => !string.Equals(x.Type, "End", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UserFriendlyException("DSL must contain at least one End node.");
        }

        if (dsl.Nodes.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != dsl.Nodes.Count)
        {
            throw new UserFriendlyException("DSL node ids must be unique.");
        }

        var refs = dsl.Nodes
            .Select(x => string.IsNullOrWhiteSpace(x.Ref) ? x.Id : x.Ref.Trim())
            .ToList();
        if (refs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != refs.Count)
        {
            throw new UserFriendlyException("DSL node Ref（引用名）must be unique.");
        }

        dsl.Edges ??= [];
        var ids = dsl.Nodes.Select(x => x.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var edge in dsl.Edges)
        {
            if (!ids.Contains(edge.Source) || !ids.Contains(edge.Target))
            {
                throw new UserFriendlyException($"Edge {edge.Source}->{edge.Target} references unknown node.");
            }
        }

        foreach (var input in dsl.Inputs)
        {
            if (string.IsNullOrWhiteSpace(input.Name))
            {
                throw new UserFriendlyException("Input parameter name is required.");
            }

            if (string.Equals(input.Source, "system", StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(input.SystemKey) &&
                string.IsNullOrWhiteSpace(input.SystemExpr))
            {
                throw new UserFriendlyException($"System input '{input.Name}' requires systemKey or systemExpr.");
            }

            if (!string.IsNullOrWhiteSpace(input.SystemExpr) &&
                !DateExpressionEvaluator.LooksLikeDateExpression(input.SystemExpr))
            {
                throw new UserFriendlyException($"Invalid systemExpr for '{input.Name}': {input.SystemExpr}");
            }
        }

        foreach (var node in dsl.Nodes.Where(x => string.Equals(x.Type, "Condition", StringComparison.OrdinalIgnoreCase)))
        {
            if (node.Items is { Count: > 0 })
            {
                var nos = node.Items.Select(x => x.No).ToList();
                if (nos.Distinct().Count() != nos.Count)
                {
                    throw new UserFriendlyException($"Condition '{node.Id}' has duplicate condition numbers.");
                }

                var edges = dsl.Edges.Where(e => string.Equals(e.Source, node.Id, StringComparison.OrdinalIgnoreCase)).ToList();
                if (edges.Count == 0)
                {
                    throw new UserFriendlyException($"Condition '{node.Id}' has no outgoing edges.");
                }

                foreach (var edge in edges.Where(e => !string.IsNullOrWhiteSpace(e.Combine)))
                {
                    ConditionCombineEvaluator.Validate(edge.Combine, nos);
                }
            }
        }

        foreach (var node in dsl.Nodes)
        {
            if (node.Entry != null && node.Entry.Items.Count > 0 && !string.IsNullOrWhiteSpace(node.Entry.Combine))
            {
                ConditionCombineEvaluator.Validate(node.Entry.Combine, node.Entry.Items.Select(x => x.No));
            }

            var type = (node.Type ?? "").Trim().ToLowerInvariant();
            if (type is "subflow" or "logiccomponent" or "component")
            {
                var key = (node.SubFlowKey ?? node.Url ?? "").Trim();
                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new UserFriendlyException($"SubFlow node '{node.Id}' requires subFlowKey.");
                }
            }
        }
    }

    private static bool Compare(object? left, object? right, string op)
    {
        if (left is IConvertible && right is IConvertible &&
            decimal.TryParse(Convert.ToString(left, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var ln) &&
            decimal.TryParse(Convert.ToString(right, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var rn))
        {
            return op switch
            {
                ">" => ln > rn,
                "<" => ln < rn,
                ">=" => ln >= rn,
                "<=" => ln <= rn,
                "==" => ln == rn,
                "!=" => ln != rn,
                _ => false
            };
        }

        var ls = Convert.ToString(left, CultureInfo.InvariantCulture) ?? "";
        var rs = Convert.ToString(right, CultureInfo.InvariantCulture) ?? "";
        return op switch
        {
            "==" => string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase),
            "!=" => !string.Equals(ls, rs, StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
            IConvertible c => Convert.ToDecimal(c, CultureInfo.InvariantCulture) != 0,
            _ => true
        };
    }

    private static string Truncate(string text, int max)
    {
        return text.Length <= max ? text : text[..max] + "...";
    }

    private sealed class NullCurrentUser : Volo.Abp.Users.ICurrentUser
    {
        public bool IsAuthenticated => false;
        public Guid? Id => null;
        public string? UserName => null;
        public string? Name => null;
        public string? SurName => null;
        public string? Email => null;
        public bool EmailVerified => false;
        public string? PhoneNumber => null;
        public bool PhoneNumberVerified => false;
        public Guid? TenantId => null;
        public string[] Roles => [];
        public System.Security.Claims.Claim? FindClaim(string claimType) => null;
        public System.Security.Claims.Claim[] FindClaims(string claimType) => [];
        public System.Security.Claims.Claim[] GetAllClaims() => [];
        public bool IsInRole(string roleName) => false;
    }
}
