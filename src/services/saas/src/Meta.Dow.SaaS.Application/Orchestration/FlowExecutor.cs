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
    private readonly IResourceCrudExecutor _resourceCrudExecutor;
    private readonly ILogger<FlowExecutor> _logger;

    public FlowExecutor(
        IHttpClientFactory httpClientFactory,
        SystemContextBuilder systemContextBuilder,
        IPermissionChecker permissionChecker,
        IDataSourceResolver dataSourceResolver,
        ICodeSandboxExecutor codeSandboxExecutor,
        IOrchestrationRabbitPublisher rabbitPublisher,
        PublishedFlowCache publishedFlowCache,
        IResourceCrudExecutor resourceCrudExecutor,
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
        _resourceCrudExecutor = resourceCrudExecutor;
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
        var outgoing = BuildOutgoing(dsl);

        var start = dsl.Nodes.First(x => string.Equals(x.Type, "Start", StringComparison.OrdinalIgnoreCase));

        try
        {
            var outcome = await RunSegmentAsync(
                nodeMap,
                outgoing,
                start.Id,
                [],
                ctx,
                isDryRun,
                callStack,
                cancellationToken,
                result.Nodes,
                filterOutputsByRole: filterOutputsByRole,
                currentUser: currentUser
            );

            if (!outcome.Ok)
            {
                result.Succeeded = false;
                result.Error = outcome.Error;
                result.VariablesJson = FlowContextResolver.SerializeContext(ctx);
                return;
            }

            if (outcome is { HitEnd: true, EndNode: { } endNode })
            {
                await FinishOutputsAsync(dsl, endNode, ctx, result, filterOutputsByRole, currentUser, isDryRun);
            }

            result.Succeeded = true;
            result.VariablesJson = FlowContextResolver.SerializeContext(ctx);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Flow execution failed");
            result.Succeeded = false;
            result.Error = ex.Message;
            result.VariablesJson = FlowContextResolver.SerializeContext(ctx);
        }
    }

    private const int DefaultMaxSteps = 200;

    private static Dictionary<string, List<FlowDslEdge>> BuildOutgoing(FlowDslDocument dsl)
    {
        return dsl.Edges
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>分段执行的终态。Error 非空即失败。</summary>
    private sealed class SegmentOutcome
    {
        public string? Error { get; set; }

        /// <summary>命中的停止节点（未执行）</summary>
        public string? Reached { get; set; }

        public bool HitEnd { get; set; }

        public FlowDslNode? EndNode { get; set; }

        public bool Ok => Error == null;
    }

    /// <summary>
    /// 单游标分段执行 —— **全流程唯一的调度原语**。
    /// 从 <paramref name="startId"/> 沿边推进，直到：
    /// 命中 <paramref name="stopIds"/> 中的节点（不执行它，直接返回）、命中 End 节点、或出错。
    /// 主链（stopIds 为空）、ForEach 循环体、Parallel 分支都复用本方法，避免出现第二套调度器。
    /// </summary>
    private async Task<SegmentOutcome> RunSegmentAsync(
        IReadOnlyDictionary<string, FlowDslNode> nodeMap,
        IReadOnlyDictionary<string, List<FlowDslEdge>> outgoing,
        string startId,
        IReadOnlyCollection<string> stopIds,
        FlowRuntimeContext ctx,
        bool isDryRun,
        HashSet<string> callStack,
        CancellationToken cancellationToken,
        List<NodeExecutionRecord> sink,
        int maxSteps = DefaultMaxSteps,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null
    )
    {
        var outcome = new SegmentOutcome();
        var stops = stopIds.Count == 0
            ? null
            : new HashSet<string>(stopIds, StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentId = startId;

        for (var step = 0; step < maxSteps; step++)
        {
            if (stops != null && stops.Contains(currentId))
            {
                outcome.Reached = currentId;
                return outcome;
            }

            if (!nodeMap.TryGetValue(currentId, out var node))
            {
                outcome.Error = $"Node '{currentId}' not found.";
                return outcome;
            }

            var isEnd = string.Equals(node.Type, "End", StringComparison.OrdinalIgnoreCase);
            if (!visited.Add(currentId) && !isEnd)
            {
                outcome.Error = $"Cycle detected at node '{currentId}'.";
                return outcome;
            }

            var record = await ExecuteNodeAsync(
                node,
                ctx,
                isDryRun,
                callStack,
                cancellationToken,
                filterOutputsByRole,
                currentUser
            );
            sink.Add(record);

            if (string.Equals(record.Status, "Failed", StringComparison.OrdinalIgnoreCase))
            {
                outcome.Error = record.Error;
                return outcome;
            }

            if (isEnd)
            {
                outcome.HitEnd = true;
                outcome.EndNode = node;
                return outcome;
            }

            if (!outgoing.TryGetValue(currentId, out var edges) || edges.Count == 0)
            {
                outcome.Error = $"Node '{currentId}' has no outgoing edge.";
                return outcome;
            }

            var nextId = ResolveNextNode(node, edges, ctx);
            if (string.IsNullOrWhiteSpace(nextId))
            {
                outcome.Error = $"No matching branch from node '{currentId}'.";
                return outcome;
            }

            currentId = nextId;
        }

        outcome.Error = "Exceeded maximum execution steps.";
        return outcome;
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
            // P0-2（评审 S7）：无契约不再整包输出 ctx.Vars —— 那是安全边界。
            // 试运行保留全量视图（设计期调试需要），正式执行一律拒绝。
            if (!isDryRun)
            {
                throw new UserFriendlyException(
                    $"流程 '{dsl.Key}' 未声明输出契约（End.finalOutputs 或根级 outputs），已拒绝输出中间变量。"
                );
            }

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
        CancellationToken cancellationToken,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null
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

            // 节点级超时（timeoutMs）。默认 NodeDefaultTimeoutMs；0 表示不限时。
            // 注意：包裹在**唯一的分发点**上，而不是每个 case 内部 —— 否则新增节点必然漏包。
            var timeoutMs = node.TimeoutMs ?? OrchestrationConsts.NodeDefaultTimeoutMs;
            using var nodeCts = timeoutMs > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            nodeCts?.CancelAfter(timeoutMs);
            var nodeToken = nodeCts?.Token ?? cancellationToken;

            var type = node.Type.ToLowerInvariant();
            if (type == "logic")
            {
                type = (node.Kind ?? "Http").ToLowerInvariant();
            }

            try
            {
                await DispatchNodeAsync(
                    type,
                    node,
                    ctx,
                    record,
                    isDryRun,
                    callStack,
                    nodeToken,
                    filterOutputsByRole,
                    currentUser
                );
            }
            catch (OperationCanceledException) when (
                nodeCts is not null && !cancellationToken.IsCancellationRequested)
            {
                throw new UserFriendlyException(
                    $"节点「{node.Id}」({node.Type}) 执行超时（{timeoutMs} ms）。可通过 timeoutMs 调整，或置 0 关闭超时。"
                );
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
    /// 节点分发。抽出独立方法，使 ExecuteNodeAsync 能在**单一点**上做超时/审计包裹。
    /// </summary>
    private async Task DispatchNodeAsync(
        string type,
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        bool isDryRun,
        HashSet<string> callStack,
        CancellationToken cancellationToken,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null
    )
    {
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
                    await ExecuteSubFlowAsync(node, ctx, record, isDryRun, callStack, cancellationToken, filterOutputsByRole, currentUser);
                    CheckFailWhen(node, ctx);
                    break;
                }

                case "foreach":
                case "loop":
                {
                    await ExecuteForEachAsync(node, ctx, record, isDryRun, callStack, cancellationToken, filterOutputsByRole, currentUser);
                    CheckFailWhen(node, ctx);
                    break;
                }

                case "parallel":
                case "when":
                {
                    await ExecuteParallelAsync(node, ctx, record, isDryRun, callStack, cancellationToken, filterOutputsByRole, currentUser);
                    CheckFailWhen(node, ctx);
                    break;
                }

                case "resourcequery":
                case "resourceget":
                case "resourcecreate":
                case "resourceupdate":
                case "resourcedelete":
                case "resourcecrud":
                case "resourcebatchcreate":
                case "resourcebatchupdate":
                case "resourcebatchdelete":
                case "resourcebatch":
                {
                    await ExecuteResourceCrudAsync(node, ctx, record, isDryRun, cancellationToken);
                    CheckFailWhen(node, ctx);
                    break;
                }

                default:
                    throw new UserFriendlyException($"Unsupported node type '{node.Type}'.");
            }
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

    private async Task ExecuteResourceCrudAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        bool isDryRun,
        CancellationToken cancellationToken
    )
    {
        var nodeInput = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        var type = (node.Type ?? "").Trim().ToLowerInvariant();
        var op = type switch
        {
            "resourcequery" => "query",
            "resourceget" => "get",
            "resourcecreate" => "create",
            "resourceupdate" => "update",
            "resourcedelete" => "delete",
            "resourcebatchcreate" => "batchcreate",
            "resourcebatchupdate" => "batchupdate",
            "resourcebatchdelete" => "batchdelete",
            _ => (node.Kind ?? "").Trim().ToLowerInvariant()
        };
        if (string.IsNullOrWhiteSpace(op) && nodeInput["op"] != null)
        {
            op = nodeInput["op"]?.ToString() ?? "";
        }

        record.InputJson = nodeInput.ToJsonString(JsonOptions);
        var raw = await _resourceCrudExecutor.ExecuteAsync(
            op, nodeInput, isDryRun, node.RetryOnWrite, cancellationToken);
        ApplyExecutableOutputs(node, raw, ctx);
        record.OutputJson = raw.ToJsonString(JsonOptions);
    }

    private async Task ExecuteSubFlowAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        bool isDryRun,
        HashSet<string> callStack,
        CancellationToken cancellationToken,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null
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
            filterOutputsByRole: filterOutputsByRole,
            currentUser: currentUser,
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

    /// <summary>控制流体（循环体一次迭代 / 并行一个分支）的执行结果。</summary>
    private sealed record ControlBodyOutcome(bool Ok, JsonNode? Output, string? Error);

    private sealed record BranchOutcome(
        string Ref,
        bool Ok,
        JsonNode? Output,
        string? Error,
        bool Optional
    );

    /// <summary>
    /// ForEach（FOR）：对数组逐项执行循环体，支持 failFast / continueOnError 与并发度。
    /// 产出契约：<c>{ items[], count, succeeded, failed[] }</c>，其中「单项产出」= 循环体 End 的 finalOutputs。
    /// 循环体两种形态：内联子图（node.body）或已发布流程（node.subFlowKey）。
    /// </summary>
    private async Task ExecuteForEachAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        bool isDryRun,
        HashSet<string> callStack,
        CancellationToken cancellationToken,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null
    )
    {
        var itemsNode = FlowContextResolver.ResolvePathNode(node.ItemsPath, ctx);
        if (itemsNode is not JsonArray array)
        {
            throw new UserFriendlyException(
                $"ForEach '{node.Id}' 的 itemsPath「{node.ItemsPath}」未解析为数组。"
            );
        }

        var bodyInputs = FlowContextResolver.MapNodeInputs(node.Inputs, ctx);
        var itemVar = string.IsNullOrWhiteSpace(node.ItemVar) ? "item" : node.ItemVar.Trim();
        var indexVar = string.IsNullOrWhiteSpace(node.IndexVar) ? "index" : node.IndexVar.Trim();
        var continueOnError = string.Equals(
            node.ItemErrorMode ?? "failFast",
            "continueOnError",
            StringComparison.OrdinalIgnoreCase
        );
        var maxIterations = Math.Clamp(
            node.MaxIterations ?? OrchestrationConsts.ForEachDefaultMaxIterations,
            1,
            OrchestrationConsts.ForEachHardMaxIterations
        );
        if (array.Count > maxIterations)
        {
            throw new UserFriendlyException(
                $"ForEach '{node.Id}' 待迭代 {array.Count} 项，超过 maxIterations {maxIterations}。"
            );
        }

        var concurrency = Math.Clamp(node.Concurrency ?? 1, 1, OrchestrationConsts.ForEachMaxConcurrency);
        EnsureControlNesting(node, "ForEach", callStack);

        record.InputJson = JsonSerializer.Serialize(
            new
            {
                bodyKind = node.LoopBody != null ? "inline" : "subflow",
                bodyKey = (node.SubFlowKey ?? "").Trim(),
                concurrency,
                count = array.Count,
                inputs = bodyInputs,
                itemErrorMode = continueOnError ? "continueOnError" : "failFast",
                itemVar,
                indexVar,
                itemsPath = node.ItemsPath
            },
            JsonOptions
        );

        var collected = new JsonNode?[array.Count];
        var failures = new JsonArray();
        var succeeded = 0;

        for (var offset = 0; offset < array.Count; offset += concurrency)
        {
            var take = Math.Min(concurrency, array.Count - offset);
            var batch = new List<Task<ControlBodyOutcome>>(take);
            for (var i = 0; i < take; i++)
            {
                batch.Add(RunIterationAsync(offset + i));
            }

            var settled = await Task.WhenAll(batch);

            for (var i = 0; i < settled.Length; i++)
            {
                var index = offset + i;
                var item = settled[i];
                if (item.Ok)
                {
                    collected[index] = item.Output?.DeepClone();
                    succeeded += 1;
                    continue;
                }

                failures.Add(
                    new JsonObject
                    {
                        ["index"] = index,
                        ["error"] = item.Error
                    }
                );

                if (!continueOnError)
                {
                    throw new UserFriendlyException(
                        $"ForEach '{node.Id}' 在第 {index} 项失败：{item.Error}"
                    );
                }
            }
        }

        var rows = new JsonArray();
        foreach (var item in collected)
        {
            rows.Add(item?.DeepClone());
        }

        var raw = new JsonObject
        {
            ["items"] = rows,
            ["count"] = array.Count,
            ["succeeded"] = succeeded,
            ["failed"] = failures
        };

        ApplyExecutableOutputs(node, raw, ctx);
        record.OutputJson = raw.ToJsonString(JsonOptions);

        Task<ControlBodyOutcome> RunIterationAsync(int index)
        {
            var bodyJson = (JsonObject)bodyInputs.DeepClone();
            bodyJson[itemVar] = array[index]?.DeepClone();
            bodyJson[indexVar] = JsonValue.Create(index);
            return RunControlBodyAsync(node, node.Id, node.LoopBody, node.SubFlowKey, bodyJson, isDryRun, callStack, cancellationToken, filterOutputsByRole, currentUser);
        }
    }

    /// <summary>
    /// Parallel（WHEN）+ 汇合（ALL / ANY / ALL_SETTLED）：并发执行各分支，按 Ref 汇合产出。
    /// 产出契约：<c>{ ok, mode, succeeded, failed[], executeMs, {各分支 ref}: 分支出参 }</c>。
    /// </summary>
    private async Task ExecuteParallelAsync(
        FlowDslNode node,
        FlowRuntimeContext ctx,
        NodeExecutionRecord record,
        bool isDryRun,
        HashSet<string> callStack,
        CancellationToken cancellationToken,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null
    )
    {
        var branches = node.Branches ?? [];
        if (branches.Count < 2)
        {
            throw new UserFriendlyException($"Parallel '{node.Id}' 至少需要 2 个分支。");
        }

        if (branches.Count > OrchestrationConsts.ParallelMaxBranches)
        {
            throw new UserFriendlyException(
                $"Parallel '{node.Id}' 分支数 {branches.Count} 超过上限 {OrchestrationConsts.ParallelMaxBranches}。"
            );
        }

        var refs = branches.Select(x => (x.Ref ?? "").Trim()).ToList();
        if (refs.Any(string.IsNullOrWhiteSpace) ||
            refs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != refs.Count)
        {
            throw new UserFriendlyException($"Parallel '{node.Id}' 的分支 ref 必须非空且唯一。");
        }

        var mode = (node.JoinMode ?? "all").Trim().ToLowerInvariant();
        var isAny = mode == "any";
        var isAllSettled = mode is "allsettled" or "all_settled";
        if (mode is not ("all" or "any" or "allsettled" or "all_settled"))
        {
            throw new UserFriendlyException(
                $"Parallel '{node.Id}' 的 joinMode「{node.JoinMode}」无效，只能是 all / any / allSettled。"
            );
        }

        EnsureControlNesting(node, "Parallel", callStack);

        record.InputJson = JsonSerializer.Serialize(
            new { branchCount = branches.Count, joinMode = mode, refs },
            JsonOptions
        );

        var sw = Stopwatch.StartNew();
        var started = branches.Select(RunBranchAsync).ToList();

        var outputs = new JsonObject();
        var failures = new JsonArray();
        var succeeded = 0;

        if (isAny)
        {
            var pending = new List<Task<BranchOutcome>>(started);
            while (pending.Count > 0)
            {
                var finished = await Task.WhenAny(pending);
                pending.Remove(finished);

                var outcome = await finished;
                if (outcome.Ok)
                {
                    outputs[outcome.Ref] = outcome.Output?.DeepClone();
                    succeeded += 1;
                    break;
                }

                failures.Add(new JsonObject { ["ref"] = outcome.Ref, ["error"] = outcome.Error });
            }

            if (succeeded == 0)
            {
                throw new UserFriendlyException(
                    $"Parallel '{node.Id}'（joinMode=any）全部分支失败，共 {failures.Count} 个。"
                );
            }
        }
        else
        {
            var settled = await Task.WhenAll(started);
            foreach (var outcome in settled)
            {
                if (outcome.Ok)
                {
                    outputs[outcome.Ref] = outcome.Output?.DeepClone();
                    succeeded += 1;
                    continue;
                }

                failures.Add(new JsonObject { ["ref"] = outcome.Ref, ["error"] = outcome.Error });
                if (!isAllSettled && !outcome.Optional)
                {
                    throw new UserFriendlyException(
                        $"Parallel '{node.Id}' 分支「{outcome.Ref}」失败：{outcome.Error}"
                    );
                }
            }
        }

        sw.Stop();

        var raw = new JsonObject
        {
            ["ok"] = failures.Count == 0,
            ["mode"] = mode,
            ["succeeded"] = succeeded,
            ["failed"] = failures,
            ["executeMs"] = sw.ElapsedMilliseconds
        };
        foreach (var (key, value) in outputs)
        {
            raw[key] = value?.DeepClone();
        }

        ApplyExecutableOutputs(node, raw, ctx);
        record.OutputJson = raw.ToJsonString(JsonOptions);

        async Task<BranchOutcome> RunBranchAsync(FlowDslBranchDsl branch)
        {
            var branchRef = (branch.Ref ?? "").Trim();

            // 分支超时逐级继承：branch.TimeoutMs → node.BranchTimeoutMs → node.TimeoutMs → 默认。
            // 未做这层继承时 BranchTimeoutMs 只是"声明即弃"，属本次要消除的缺陷类型。
            var branchTimeoutMs = branch.TimeoutMs
                                  ?? node.BranchTimeoutMs
                                  ?? node.TimeoutMs
                                  ?? OrchestrationConsts.NodeDefaultTimeoutMs;
            using var branchCts = branchTimeoutMs > 0
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : null;
            branchCts?.CancelAfter(branchTimeoutMs);
            var branchToken = branchCts?.Token ?? cancellationToken;

            try
            {
                var branchInputs = FlowContextResolver.MapNodeInputs(branch.Inputs, ctx);
                var outcome = await RunControlBodyAsync(
                    node,
                    branchRef,
                    branch.Body,
                    branch.SubFlowKey,
                    branchInputs,
                    isDryRun,
                    callStack,
                    branchToken,
                    filterOutputsByRole,
                    currentUser
                );
                return new BranchOutcome(branchRef, outcome.Ok, outcome.Output, outcome.Error, branch.Optional);
            }
            catch (OperationCanceledException) when (
                branchCts is not null && !cancellationToken.IsCancellationRequested)
            {
                return new BranchOutcome(
                    branchRef,
                    false,
                    null,
                    $"分支「{branchRef}」超时（{branchTimeoutMs} ms）。",
                    branch.Optional
                );
            }
            catch (Exception ex)
            {
                return new BranchOutcome(branchRef, false, null, ex.Message, branch.Optional);
            }
        }
    }

    /// <summary>
    /// 执行控制流节点的「体」（循环一次迭代 / 并行一个分支）。
    /// 走标准 ExecuteAsync 通道，因此体的 End.finalOutputs、map、aggregate、visibleTo 全部生效。
    /// 并发安全：为每次调用复制一份调用栈，避免并发下共享 HashSet 的 add/remove 竞态与误判成环。
    /// </summary>
    private async Task<ControlBodyOutcome> RunControlBodyAsync(
        FlowDslNode host,
        string scopeLabel,
        FlowDslSubGraphDsl? inlineBody,
        string? subFlowKey,
        JsonObject bodyInput,
        bool isDryRun,
        HashSet<string> callStack,
        CancellationToken cancellationToken,
        bool filterOutputsByRole = false,
        Volo.Abp.Users.ICurrentUser? currentUser = null
    )
    {
        var bodyJson = bodyInput.ToJsonString(JsonOptions);
        var scopeStack = new HashSet<string>(callStack, StringComparer.OrdinalIgnoreCase);

        if (inlineBody is { Nodes.Count: > 0 })
        {
            var subDsl = BuildInlineBodyDsl(host, scopeLabel, inlineBody);
            var inline = await ExecuteAsync(
                JsonSerializer.Serialize(subDsl, JsonOptions),
                bodyJson,
                isDryRun,
                filterOutputsByRole: filterOutputsByRole,
                currentUser: currentUser,
                cancellationToken: cancellationToken,
                skipValidation: true,
                callStack: scopeStack
            );
            return new ControlBodyOutcome(inline.Succeeded, ParseJsonNode(inline.OutputDataJson), inline.Error);
        }

        var key = (subFlowKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new UserFriendlyException(
                $"控制流节点 '{host.Id}' 的「{scopeLabel}」必须配置 body（内联子图）或 subFlowKey（已发布流程）。"
            );
        }

        if (callStack.Contains(key))
        {
            throw new UserFriendlyException($"控制流 '{host.Id}' 的「{scopeLabel}」引用了调用栈中的流程：{key}");
        }

        var snap = await _publishedFlowCache.GetByKeyAsync(key);
        var child = await ExecuteAsync(
            snap.DslJson,
            bodyJson,
            isDryRun,
            filterOutputsByRole: filterOutputsByRole,
            currentUser: currentUser,
            cancellationToken: cancellationToken,
            skipValidation: true,
            callStack: scopeStack
        );
        return new ControlBodyOutcome(child.Succeeded, ParseJsonNode(child.OutputDataJson), child.Error);
    }

    private static void EnsureControlNesting(FlowDslNode node, string label, HashSet<string> callStack)
    {
        if (callStack.Count >= OrchestrationConsts.MaxControlNestingDepth)
        {
            throw new UserFriendlyException(
                $"{label} '{node.Id}' 嵌套深度超过上限 {OrchestrationConsts.MaxControlNestingDepth}。"
            );
        }
    }

    private static JsonNode? ParseJsonNode(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }

    /// <summary>
    /// 把内联子图包装成可执行 DSL。Key 取 host 引用名 + 作用域，纳入统一调用栈防环口径。
    /// 体必须自带 Start 与 End（End.finalOutputs 即该项产出契约）。
    /// </summary>
    private static FlowDslDocument BuildInlineBodyDsl(
        FlowDslNode host,
        string scopeLabel,
        FlowDslSubGraphDsl body
    )
    {
        var hostRef = string.IsNullOrWhiteSpace(host.Ref) ? host.Id : host.Ref.Trim();

        // 经 JSON 往返深拷贝，避免与父图共享 JsonNode 实例（并发执行下会被改写）
        var nodes = JsonSerializer.Deserialize<List<FlowDslNode>>(
            JsonSerializer.Serialize(body.Nodes, JsonOptions),
            JsonOptions
        ) ?? [];
        var edges = JsonSerializer.Deserialize<List<FlowDslEdge>>(
            JsonSerializer.Serialize(body.Edges, JsonOptions),
            JsonOptions
        ) ?? [];
        var inputs = JsonSerializer.Deserialize<List<FlowInputParameterDsl>>(
            JsonSerializer.Serialize(body.Inputs, JsonOptions),
            JsonOptions
        ) ?? [];

        return new FlowDslDocument
        {
            Version = OrchestrationConsts.DslVersion,
            Key = $"{hostRef}#{scopeLabel}",
            Inputs = inputs,
            Outputs = [],
            Nodes = nodes,
            Edges = edges
        };
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

        // P0-1（评审 S/C2）：节点类型白名单。dispatch 对未知类型一律抛 500，
        // 这里把"发布成功、调用即炸"提前变成发布期错误。
        foreach (var node in dsl.Nodes)
        {
            var kind = ResolveNodeKind(node).Trim().ToLowerInvariant();
            if (!SupportedNodeKinds.Contains(kind))
            {
                throw new UserFriendlyException(
                    $"节点 '{node.Id}' 的类型不受支持：'{node.Type}'" +
                    (string.IsNullOrWhiteSpace(node.Kind) ? "" : $"（有效类型 '{kind}'）") +
                    "。受支持类型：" + string.Join(", ", SupportedNodeKinds.OrderBy(x => x, StringComparer.Ordinal)) +
                    "。多路分支请用 Condition（多出边 + combine / isDefault）。"
                );
            }
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

        ValidateOutEdgeCardinality(dsl);

        foreach (var node in dsl.Nodes)
        {
            ValidateControlNode(node);
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

            if (type == "resourcequery")
            {
                var names = node.Inputs?.Select(x => x.Name).ToList() ?? [];
                var hasResource = names.Any(x => string.Equals(x, "resourceCode", StringComparison.OrdinalIgnoreCase));
                var hasTable =
                    names.Any(x => string.Equals(x, "dataSourceCode", StringComparison.OrdinalIgnoreCase)) &&
                    names.Any(x => string.Equals(x, "tableName", StringComparison.OrdinalIgnoreCase));
                if (!hasResource && !hasTable)
                {
                    throw new UserFriendlyException(
                        $"ResourceQuery node '{node.Id}' requires resourceCode or dataSourceCode+tableName."
                    );
                }
            }
            else if (type is "resourceget" or "resourcecreate" or "resourceupdate"
                or "resourcedelete" or "resourcecrud"
                or "resourcebatchcreate" or "resourcebatchupdate" or "resourcebatchdelete"
                or "resourcebatch")
            {
                if (node.Inputs is not { Count: > 0 } ||
                    node.Inputs.All(x => !string.Equals(x.Name, "resourceCode", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new UserFriendlyException(
                        $"Resource node '{node.Id}' requires an input named resourceCode."
                    );
                }

                // 批量节点必须显式声明 items 入参：批量语义对「空集」和「漏传」的处置不同，
                // 漏传会静默变成 0 行影响，属典型的「配置被接受但不生效」，故在此拦截。
                if (type is "resourcebatchcreate" or "resourcebatchupdate" or "resourcebatchdelete")
                {
                    var inputs = node.Inputs ?? [];
                    if (inputs.All(x => !string.Equals(x.Name, "items", StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new UserFriendlyException(
                            $"Batch resource node '{node.Id}' requires an input named items (array)."
                        );
                    }

                    if (string.Equals(type, "resourcebatchdelete", StringComparison.OrdinalIgnoreCase) &&
                        node.Inputs!.Any(x => string.Equals(x.Name, "onDelete", StringComparison.OrdinalIgnoreCase)))
                    {
                        throw new UserFriendlyException(
                            $"Batch resource node '{node.Id}' 不支持 onDelete：批量删除固定走软删（与单条 delete 一致）。"
                        );
                    }
                }
            }
        }

        ValidateOutputContract(dsl);
    }

    /// <summary>
    /// 输出契约与字段可见性校验（P0-2 / P0-3，评审 S7 / S12）。
    ///
    /// P0-2：无最终契约时引擎不再回退输出全部中间变量（那是安全边界）——
    /// 根级 <c>outputs</c> 或**每个** End 节点的 <c>finalOutputs</c> 至少声明其一，
    /// 否则发布被拒。运行期 <see cref="FinishOutputsAsync"/> 仍有兜底报错。
    ///
    /// P0-3：<c>visibleTo.mode</c> 只接受 all/none/roles/permissions（拼错即拒）；
    /// <c>roleIds</c> 不受支持（ICurrentUser 只暴露角色名，配了恒不匹配 → 显式拒绝而非静默失效）。
    /// </summary>
    private static void ValidateOutputContract(FlowDslDocument dsl)
    {
        var endNodes = dsl.Nodes
            .Where(x => string.Equals(x.Type, "End", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var hasRootOutputs = dsl.Outputs is { Count: > 0 };
        if (!hasRootOutputs && endNodes.Any(e => e.FinalOutputs is not { Count: > 0 }))
        {
            throw new UserFriendlyException(
                "流程必须声明输出契约：根级 outputs，或每个 End 节点的 finalOutputs。" +
                "未声明时引擎不再回退输出全部中间变量（契约优先，防内部变量整包外泄）。"
            );
        }

        foreach (var output in dsl.Outputs)
        {
            ValidateVisibleTo($"outputs.{output.Name}", output.VisibleTo);
        }

        foreach (var end in endNodes)
        {
            foreach (var output in end.FinalOutputs ?? [])
            {
                ValidateVisibleTo($"End('{end.Id}').finalOutputs.{output.Name}", output.VisibleTo);
            }
        }

        // 注：节点级 outputs（FlowNodeBindingDsl）不承载 visibleTo —— 出参可见性只在
        // 「契约层」（根级 outputs / End.finalOutputs）定义，故此处无需校验节点绑定。
    }

    private static readonly HashSet<string> KnownVisibleToModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "all",
        "none",
        "roles",
        "permissions"
    };

    private static void ValidateVisibleTo(string owner, FlowVisibleToDsl? visibleTo)
    {
        if (visibleTo == null)
        {
            return;
        }

        var mode = (visibleTo.Mode ?? "").Trim();
        if (!KnownVisibleToModes.Contains(mode))
        {
            throw new UserFriendlyException(
                $"{owner} 的 visibleTo.mode '{visibleTo.Mode}' 不受支持（受支持：all / none / roles / permissions）。"
            );
        }

        if (visibleTo.RoleIds is { Count: > 0 })
        {
            throw new UserFriendlyException(
                $"{owner} 的 visibleTo.roleIds 不受支持：运行期只有角色名（ICurrentUser.Roles），roleIds 恒不匹配。请改用 roleNames。"
            );
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

    /// <summary>
    /// 出边基数校验（P0-3）。执行器对非 Condition 节点走 <c>edges.First()</c>，多余出边会被**静默丢弃**。
    /// 这里把"看起来正常却只跑一条边"的隐性故障提前变成显式错误。
    /// 只校验从 Start 可达的节点：不可达节点的脏边不影响运行时行为。
    /// </summary>
    private static void ValidateOutEdgeCardinality(FlowDslDocument dsl)
    {
        // Switch 曾在此白名单里，但它没有 dispatch 分支（发布成功、调用即 500）——
        // 节点类型白名单（P0-1）现在直接拒绝该类型，多出边仅 Condition 合法。
        var multiOutAllowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Condition"
        };

        var outgoing = dsl.Edges
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var reachable = CollectReachable(dsl);

        foreach (var node in dsl.Nodes)
        {
            if (!reachable.Contains(node.Id))
            {
                continue;
            }

            var type = ResolveNodeKind(node);
            var count = outgoing.TryGetValue(node.Id, out var n) ? n : 0;

            if (string.Equals(type, "End", StringComparison.OrdinalIgnoreCase))
            {
                if (count > 0)
                {
                    throw new UserFriendlyException($"End 节点 '{node.Id}' 不允许有出边。");
                }

                continue;
            }

            if (count == 0)
            {
                throw new UserFriendlyException($"节点 '{node.Id}'（{type}）没有任何出边。");
            }

            if (count > 1 && !multiOutAllowed.Contains(type))
            {
                throw new UserFriendlyException(
                    $"节点 '{node.Id}'（{type}）有 {count} 条出边，但只有第 1 条会生效，其余会被静默忽略。" +
                    "多路枚举分支请改用 Condition（多出边 + combine / isDefault）；并行请改用 Parallel（branches）。"
                );
            }
        }
    }

    /// <summary>节点的**有效类型**：Logic 节点折算为其 Kind（与 ExecuteNodeAsync 的 dispatch 口径一致）。</summary>
    private static string ResolveNodeKind(FlowDslNode node)
    {
        var type = (node.Type ?? "").Trim();
        if (string.Equals(type, "Logic", StringComparison.OrdinalIgnoreCase))
        {
            return string.IsNullOrWhiteSpace(node.Kind) ? "Http" : node.Kind.Trim();
        }

        return type;
    }

    /// <summary>
    /// 节点类型白名单（P0-1，评审 C2）。与 <see cref="DispatchNodeAsync"/> 的 case 清单**逐一对应**：
    /// 新增 dispatch 分支时必须同步加这里；否则发布期会拒绝该类型。
    /// 注意：<c>sql</c> / <c>switch</c> / <c>wait</c> 刻意不在名单里 —— 引擎没有对应分支，
    /// 配了就是"发布成功、调用即 500"，宁可发布失败。
    /// </summary>
    public static readonly HashSet<string> SupportedNodeKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        // 流程骨架
        "start",
        "end",
        // 简单动作
        "log",
        "assign",
        "setvariable",
        "mask",
        "throw",
        "assert",
        "condition",
        // 集成
        "http",
        "httpcall",
        "code",
        "rabbitmqpublish",
        "rabbitpublish",
        "broadcast",
        // 复用与控制流（统一调度器）
        "subflow",
        "logiccomponent",
        "component",
        "foreach",
        "loop",
        "parallel",
        "when",
        // 资源 CRUD（含 R4 批量三入口）
        "resourcequery",
        "resourceget",
        "resourcecreate",
        "resourceupdate",
        "resourcedelete",
        "resourcecrud",
        "resourcebatchcreate",
        "resourcebatchupdate",
        "resourcebatchdelete",
        "resourcebatch"
    };

    /// <summary>
    /// 判定节点类型是否属于「写操作」且可通过 retryOnWrite 开启重试。
    /// 目前覆盖资源写（create/update/delete）与 SQL 写（Code 节点内 db.execute/batch 不在此列，
    /// 因其重试开关需由脚本自身控制）；其余节点类型拒绝该字段，避免"配了不生效"。
    /// </summary>
    private static bool SupportWriteRetry(string type) =>
        type.Equals("resourcecreate", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("resourceupdate", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("resourcedelete", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("resourcebatchcreate", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("resourcebatchupdate", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("resourcebatchdelete", StringComparison.OrdinalIgnoreCase) ||
        type.Equals("resourcebatch", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 从节点入参中解析幂等键。约定：入参名 <c>idempotencyKey</c>（大小写不敏感）。
    /// 字面量（{ literal: "..." } / 字符串）与路径绑定（input.x / sys.Now …）皆视为已提供，
    /// 因为绑定的实际取值只有运行时才知道 —— 校验层只保证"声明了"，不保证"非空"。
    /// </summary>
    private static string? ResolveIdempotencyKey(FlowDslNode node)
    {
        if (node.Inputs is not { Count: > 0 })
        {
            return null;
        }

        foreach (var input in node.Inputs)
        {
            if (!string.Equals(input.Name, "idempotencyKey", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (input.From is null)
            {
                return null; // 声明了入参但没有来源 → 视为未提供
            }

            // 字面量形式：{ "literal": "..." }
            if (input.From is JsonObject obj && obj.TryGetPropertyValue("literal", out var literal))
            {
                var text = literal?.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }

            // 路径绑定形式（input.x / sys.Now / results.n.f …）：运行期才求值，声明即视为已提供。
            var path = input.From.ToString();
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }

        return null;
    }

    /// <summary>从 Start 出发做可达性分析（沿出边 BFS）。</summary>
    private static HashSet<string> CollectReachable(FlowDslDocument dsl)
    {
        var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var start = dsl.Nodes.FirstOrDefault(x =>
            string.Equals(x.Type, "Start", StringComparison.OrdinalIgnoreCase));
        if (start == null)
        {
            return reachable;
        }

        var outgoing = dsl.Edges
            .GroupBy(x => x.Source, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Target).ToList(), StringComparer.OrdinalIgnoreCase);

        var queue = new Queue<string>();
        queue.Enqueue(start.Id);
        reachable.Add(start.Id);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!outgoing.TryGetValue(id, out var targets))
            {
                continue;
            }

            foreach (var target in targets)
            {
                if (reachable.Add(target))
                {
                    queue.Enqueue(target);
                }
            }
        }

        return reachable;
    }

    /// <summary>控制流原语（ForEach / Parallel）的结构校验：必需字段、枚举取值、子图完整性。</summary>
    private static void ValidateControlNode(FlowDslNode node)
    {
        var type = ResolveNodeKind(node);

        // 节点级超时校验：不允许"配了不生效"或"配了等于没配"的模糊取值。
        if (node.TimeoutMs is { } timeoutMs)
        {
            if (timeoutMs < 0 ||
                (timeoutMs > 0 && timeoutMs < OrchestrationConsts.NodeMinTimeoutMs) ||
                timeoutMs > OrchestrationConsts.NodeMaxTimeoutMs)
            {
                throw new UserFriendlyException(
                    $"节点 '{node.Id}' 的 timeoutMs 必须是 0（不限时）或 {OrchestrationConsts.NodeMinTimeoutMs}..{OrchestrationConsts.NodeMaxTimeoutMs} 之间的毫秒数。"
                );
            }
        }

        // branchTimeoutMs 只对 Parallel 有意义：配在其它节点上会被静默忽略，故直接拒绝。
        if (node.BranchTimeoutMs is { } branchTimeoutMs)
        {
            if (!type.Equals("Parallel", StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException(
                    $"节点 '{node.Id}'（{type}）不支持 branchTimeoutMs，该字段仅在 Parallel 上生效。"
                );
            }

            if (branchTimeoutMs < 0 ||
                (branchTimeoutMs > 0 && branchTimeoutMs < OrchestrationConsts.NodeMinTimeoutMs) ||
                branchTimeoutMs > OrchestrationConsts.NodeMaxTimeoutMs)
            {
                throw new UserFriendlyException(
                    $"Parallel '{node.Id}' 的 branchTimeoutMs 必须是 0 或 {OrchestrationConsts.NodeMinTimeoutMs}..{OrchestrationConsts.NodeMaxTimeoutMs} 之间的毫秒数。"
                );
            }
        }

        // retryOnWrite 是「危险开关」：写重试可能造成重复副作用。启用必须同时提供幂等键，
        // 否则拒绝发布 —— 把纪律落到代码里，而不是只写在文档里。
        if (node.RetryOnWrite)
        {
            if (!SupportWriteRetry(type))
            {
                throw new UserFriendlyException(
                    $"节点 '{node.Id}'（{type}）不支持 retryOnWrite，该字段仅对资源写/SQL 写类节点生效。"
                );
            }

            if (string.IsNullOrWhiteSpace(ResolveIdempotencyKey(node)))
            {
                throw new UserFriendlyException(
                    $"节点 '{node.Id}' 声明了 retryOnWrite，但未提供幂等键（idempotencyKey）。" +
                    "写操作重试可能造成重复副作用，必须配置幂等键后才能启用。"
                );
            }
        }

        if (type.Equals("ForEach", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("Loop", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(node.ItemsPath))
            {
                throw new UserFriendlyException($"ForEach '{node.Id}' 必须配置 itemsPath。");
            }

            if (node.LoopBody is { } body)
            {
                ValidateSubGraph(node, "body", body);
            }
            else if (string.IsNullOrWhiteSpace(node.SubFlowKey))
            {
                throw new UserFriendlyException(
                    $"ForEach '{node.Id}' 必须配置 body（内联子图）或 subFlowKey（已发布流程）。"
                );
            }

            var maxIterations = node.MaxIterations ?? OrchestrationConsts.ForEachDefaultMaxIterations;
            if (maxIterations < 1 || maxIterations > OrchestrationConsts.ForEachHardMaxIterations)
            {
                throw new UserFriendlyException(
                    $"ForEach '{node.Id}' 的 maxIterations 必须在 1..{OrchestrationConsts.ForEachHardMaxIterations} 之间。"
                );
            }

            if (node.Concurrency is { } concurrency &&
                (concurrency < 1 || concurrency > OrchestrationConsts.ForEachMaxConcurrency))
            {
                throw new UserFriendlyException(
                    $"ForEach '{node.Id}' 的 concurrency 必须在 1..{OrchestrationConsts.ForEachMaxConcurrency} 之间。"
                );
            }

            var itemErrorMode = (node.ItemErrorMode ?? "failFast").Trim();
            if (!itemErrorMode.Equals("failFast", StringComparison.OrdinalIgnoreCase) &&
                !itemErrorMode.Equals("continueOnError", StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException(
                    $"ForEach '{node.Id}' 的 itemErrorMode 只能是 failFast / continueOnError。"
                );
            }
        }

        if (type.Equals("Parallel", StringComparison.OrdinalIgnoreCase))
        {
            var branches = node.Branches ?? [];
            if (branches.Count < 2)
            {
                throw new UserFriendlyException($"Parallel '{node.Id}' 至少需要 2 个分支。");
            }

            if (branches.Count > OrchestrationConsts.ParallelMaxBranches)
            {
                throw new UserFriendlyException(
                    $"Parallel '{node.Id}' 分支数超过上限 {OrchestrationConsts.ParallelMaxBranches}。"
                );
            }

            var refs = new List<string>();
            foreach (var branch in branches)
            {
                var branchRef = (branch.Ref ?? "").Trim();
                if (string.IsNullOrWhiteSpace(branchRef))
                {
                    throw new UserFriendlyException($"Parallel '{node.Id}' 的分支 ref 不能为空。");
                }

                refs.Add(branchRef);

                if (branch.Body is { } branchBody)
                {
                    ValidateSubGraph(node, $"branch:{branchRef}", branchBody);
                }
                else if (string.IsNullOrWhiteSpace(branch.SubFlowKey))
                {
                    throw new UserFriendlyException(
                        $"Parallel '{node.Id}' 分支「{branchRef}」必须配置 body 或 subFlowKey。"
                    );
                }
            }

            if (refs.Distinct(StringComparer.OrdinalIgnoreCase).Count() != refs.Count)
            {
                throw new UserFriendlyException($"Parallel '{node.Id}' 的分支 ref 必须唯一。");
            }

            var joinMode = (node.JoinMode ?? "all").Trim();
            if (!joinMode.Equals("all", StringComparison.OrdinalIgnoreCase) &&
                !joinMode.Equals("any", StringComparison.OrdinalIgnoreCase) &&
                !joinMode.Equals("allSettled", StringComparison.OrdinalIgnoreCase))
            {
                throw new UserFriendlyException(
                    $"Parallel '{node.Id}' 的 joinMode 只能是 all / any / allSettled。"
                );
            }
        }
    }

    /// <summary>
    /// 内联子图校验。子图会被包装成独立 DSL 执行，所以必须自带 Start / End，
    /// 并递归套用与主图完全相同的规则（含出边基数）。
    /// </summary>
    private static void ValidateSubGraph(FlowDslNode host, string scope, FlowDslSubGraphDsl body)
    {
        if (body.Nodes.Count == 0)
        {
            throw new UserFriendlyException($"节点 '{host.Id}' 的「{scope}」子图为空。");
        }

        if (body.Nodes.Count(x => string.Equals(x.Type, "Start", StringComparison.OrdinalIgnoreCase)) != 1)
        {
            throw new UserFriendlyException($"节点 '{host.Id}' 的「{scope}」子图必须包含恰好 1 个 Start 节点。");
        }

        if (body.Nodes.All(x => !string.Equals(x.Type, "End", StringComparison.OrdinalIgnoreCase)))
        {
            throw new UserFriendlyException($"节点 '{host.Id}' 的「{scope}」子图必须包含至少 1 个 End 节点。");
        }

        if (body.Nodes.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != body.Nodes.Count)
        {
            throw new UserFriendlyException($"节点 '{host.Id}' 的「{scope}」子图节点 Id 必须唯一。");
        }

        ValidateDsl(
            new FlowDslDocument
            {
                Version = OrchestrationConsts.DslVersion,
                Nodes = body.Nodes,
                Edges = body.Edges
            }
        );
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
