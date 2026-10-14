using System;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Users;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// FlowExecutor 控制流原语测试（P0-1 / P0-2 / P0-3）。
///
/// 覆盖三层：
/// 1. <see cref="FlowExecutor.ValidateDsl"/> —— 出边基数（静默失效）、可达性、ForEach / Parallel 结构约束；
/// 2. 执行 —— ForEach（成功 / failFast / continueOnError / 并发 / 上限 / 非数组）与
///    Parallel（all / any / allSettled / 非 optional 失败）；
/// 3. 种子守卫 —— 四条已发布演示流程的 DSL 必须能通过校验（含本次收紧的基数规则）。
/// </summary>
public class FlowExecutorControlFlowTests
{
    // ------------------------------------------------------------------
    // 校验：出边基数（P0-3 —— 非 Condition 多出边曾被执行器静默丢弃）
    // ------------------------------------------------------------------

    [Fact]
    public void Non_condition_node_with_multiple_out_edges_is_rejected()
    {
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "a", "type": "Assign", "inputs": [{ "name": "x", "from": { "literal": 1 } }] },
                { "id": "b1", "type": "Log", "message": "1" },
                { "id": "b2", "type": "Log", "message": "2" },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "a" },
                { "source": "a", "target": "b1" },
                { "source": "a", "target": "b2" },
                { "source": "b1", "target": "end" },
                { "source": "b2", "target": "end" }
              ]
            }
            """;

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("静默忽略", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Condition_node_with_multiple_out_edges_is_allowed()
    {
        const string dsl =
            """
            {
              "outputs": [
                { "name": "x", "type": "number", "from": "x", "visibleTo": { "mode": "all" } }
              ],
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "c", "type": "Condition", "items": [{ "no": 1, "left": "input.x", "op": "gt", "right": 1 }] },
                { "id": "hi", "type": "Log", "message": "hi" },
                { "id": "lo", "type": "Log", "message": "lo" },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "c" },
                { "source": "c", "target": "hi", "combine": "1" },
                { "source": "c", "target": "lo", "isDefault": true },
                { "source": "hi", "target": "end" },
                { "source": "lo", "target": "end" }
              ]
            }
            """;

        FlowExecutor.ValidateDsl(ParseDsl(dsl));
    }

    [Fact]
    public void End_node_with_out_edge_is_rejected()
    {
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "end" },
                { "source": "end", "target": "start" }
              ]
            }
            """;

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("不允许有出边", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreachable_node_without_out_edge_is_ignored()
    {
        const string dsl =
            """
            {
              "outputs": [
                { "name": "x", "type": "number", "from": "x", "visibleTo": { "mode": "all" } }
              ],
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "end", "type": "End" },
                { "id": "orphan", "type": "Log", "message": "unreachable" }
              ],
              "edges": [
                { "source": "start", "target": "end" }
              ]
            }
            """;

        FlowExecutor.ValidateDsl(ParseDsl(dsl));
    }

    // ------------------------------------------------------------------
    // 校验：ForEach / Parallel 结构约束
    // ------------------------------------------------------------------

    [Fact]
    public void Foreach_without_items_path_is_rejected()
    {
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "each", "type": "ForEach", "subFlowKey": "some-flow" },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "each" },
                { "source": "each", "target": "end" }
              ]
            }
            """;

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("itemsPath", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Foreach_without_body_and_subflow_is_rejected()
    {
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "each", "type": "ForEach", "itemsPath": "input.lines" },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "each" },
                { "source": "each", "target": "end" }
              ]
            }
            """;

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("subFlowKey", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("whatever")]
    [InlineData("ignore")]
    public void Foreach_with_invalid_item_error_mode_is_rejected(string mode)
    {
        var dsl = ForeachDsl(mode, OkLoopBody);

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("itemErrorMode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Foreach_with_out_of_range_concurrency_is_rejected()
    {
        var dsl = ForeachDsl("failFast", OkLoopBody, concurrency: 999);

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("concurrency", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Foreach_with_out_of_range_max_iterations_is_rejected()
    {
        var dsl = ForeachDsl("failFast", OkLoopBody, maxIterations: 0);

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("maxIterations", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Foreach_body_without_start_is_rejected()
    {
        const string body =
            """
            {
              "nodes": [
                { "id": "a", "type": "Assign", "inputs": [{ "name": "x", "from": { "literal": 1 } }] },
                { "id": "e", "type": "End" }
              ],
              "edges": [{ "source": "a", "target": "e" }]
            }
            """;

        var dsl = ForeachDsl("failFast", body);

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("Start", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parallel_with_single_branch_is_rejected()
    {
        var dsl = ParallelDsl("all", $"[ {BranchJson("only", OkBranchBody)} ]");

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("至少需要 2 个分支", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parallel_with_duplicate_branch_refs_is_rejected()
    {
        var dsl = ParallelDsl(
            "all",
            $"[ {BranchJson("dup", OkBranchBody)}, {BranchJson("dup", OkBranchBody)} ]"
        );

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("必须唯一", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parallel_branch_without_body_and_subflow_is_rejected()
    {
        var dsl = ParallelDsl("all", """[ { "ref": "a" }, { "ref": "b", "subFlowKey": "x" } ]""");

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("必须配置 body 或 subFlowKey", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Parallel_with_invalid_join_mode_is_rejected()
    {
        var dsl = ParallelDsl(
            "race",
            $"[ {BranchJson("a", OkBranchBody)}, {BranchJson("b", OkBranchBody)} ]"
        );

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("joinMode", ex.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // 执行：ForEach
    // ------------------------------------------------------------------

    [Fact]
    public async Task Foreach_collects_one_output_per_item_in_order()
    {
        var result = await RunAsync(
            ForeachDsl("continueOnError", OkLoopBody),
            """{ "lines": [ { "sku": "A" }, { "sku": "B" }, { "sku": "C" } ] }"""
        );

        var data = Parse(result.OutputDataJson);
        Assert.Equal("3", data["count"]!.ToJsonString());
        Assert.Equal("3", data["succeeded"]!.ToJsonString());
        Assert.Equal("[]", data["failed"]!.ToJsonString());

        var items = data["items"]!.AsArray();
        Assert.Equal(3, items.Count);
        Assert.Equal("A", items[0]!["sku"]!.GetValue<string>());
        Assert.Equal("B", items[1]!["sku"]!.GetValue<string>());
        Assert.Equal("C", items[2]!["sku"]!.GetValue<string>());

        // indexVar 从 0 起
        Assert.Equal("0", items[0]!["seq"]!.ToJsonString());
        Assert.Equal("2", items[2]!["seq"]!.ToJsonString());
    }

    [Fact]
    public async Task Foreach_empty_array_yields_zero_iterations_without_failing()
    {
        var result = await RunAsync(ForeachDsl("failFast", OkLoopBody), """{ "lines": [] }""");

        var data = Parse(result.OutputDataJson);
        Assert.Equal("0", data["count"]!.ToJsonString());
        Assert.Equal("0", data["succeeded"]!.ToJsonString());
        Assert.Empty(data["items"]!.AsArray());
    }

    [Fact]
    public async Task Foreach_fail_fast_stops_on_first_failing_item()
    {
        var result = await RunRawAsync(
            ForeachDsl("failFast", ThrowLoopBody),
            """{ "lines": [ { "sku": "A" }, { "sku": "B", "fail": "boom" }, { "sku": "C" } ] }"""
        );

        Assert.False(result.Succeeded);
        Assert.Contains("第 1 项失败", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Foreach_continue_on_error_collects_failures_and_keeps_going()
    {
        var result = await RunAsync(
            ForeachDsl("continueOnError", ThrowLoopBody),
            """{ "lines": [ { "sku": "A" }, { "sku": "B", "fail": "boom" }, { "sku": "C" } ] }"""
        );

        var data = Parse(result.OutputDataJson);
        Assert.Equal("3", data["count"]!.ToJsonString());
        Assert.Equal("2", data["succeeded"]!.ToJsonString());

        var failed = data["failed"]!.AsArray();
        Assert.Single(failed);
        Assert.Equal("1", failed[0]!["index"]!.ToJsonString());
        Assert.False(string.IsNullOrWhiteSpace(failed[0]!["error"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Foreach_concurrency_preserves_input_order()
    {
        var result = await RunAsync(
            ForeachDsl("failFast", OkLoopBody, concurrency: 4),
            """{ "lines": [ { "sku": "A" }, { "sku": "B" }, { "sku": "C" }, { "sku": "D" }, { "sku": "E" } ] }"""
        );

        var items = Parse(result.OutputDataJson)["items"]!.AsArray();
        Assert.Equal(5, items.Count);
        Assert.Equal("A", items[0]!["sku"]!.GetValue<string>());
        Assert.Equal("E", items[4]!["sku"]!.GetValue<string>());
        Assert.Equal("4", items[4]!["seq"]!.ToJsonString());
    }

    [Fact]
    public async Task Foreach_item_count_over_max_iterations_fails()
    {
        var result = await RunRawAsync(
            ForeachDsl("failFast", OkLoopBody, maxIterations: 2),
            """{ "lines": [ { "sku": "A" }, { "sku": "B" }, { "sku": "C" } ] }"""
        );

        Assert.False(result.Succeeded);
        Assert.Contains("超过 maxIterations", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Foreach_with_non_array_items_path_fails()
    {
        // 入参声明为 object 才能绕过 InputRuleValidator 的类型校验，从而真正测到 ForEach 自身的数组守卫
        var result = await RunRawAsync(
            ForeachDsl("failFast", OkLoopBody, inputType: "object"),
            """{ "lines": { "not": "an array" } }"""
        );

        Assert.False(result.Succeeded);
        Assert.Contains("未解析为数组", result.Error, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // 执行：Parallel
    // ------------------------------------------------------------------

    [Fact]
    public async Task Parallel_all_merges_every_branch_output()
    {
        var result = await RunAsync(
            ParallelDsl(
                "all",
                $"[ {BranchJson("b1", OkBranchBody)}, {BranchJson("b2", OkBranchBody)}, {BranchJson("b3", OkBranchBody)} ]"
            ),
            """{ "orderId": "ORD-1" }"""
        );

        var data = Parse(result.OutputDataJson);
        Assert.Equal("true", data["ok"]!.ToJsonString());
        Assert.Equal("3", data["succeeded"]!.ToJsonString());
        Assert.Equal("[]", data["failed"]!.AsArray().ToJsonString());

        // 分支产出按 ref 合入 results.fan.{ref}
        Assert.Equal("ORD-1", data["first"]!["orderId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Parallel_all_fails_when_a_required_branch_fails()
    {
        var result = await RunRawAsync(
            ParallelDsl(
                "all",
                $"[ {BranchJson("b1", OkBranchBody)}, {BranchJson("b2", FailBranchBody)} ]"
            ),
            """{ "orderId": "ORD-1" }"""
        );

        Assert.False(result.Succeeded);
        Assert.Contains("分支「b2」失败", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Parallel_all_settled_reports_failure_without_aborting()
    {
        var result = await RunAsync(
            ParallelDsl(
                "allSettled",
                $"[ {BranchJson("b1", OkBranchBody)}, {BranchJson("b2", FailBranchBody)}, {BranchJson("b3", OkBranchBody)} ]"
            ),
            """{ "orderId": "ORD-1" }"""
        );

        var data = Parse(result.OutputDataJson);
        Assert.Equal("false", data["ok"]!.ToJsonString());
        Assert.Equal("2", data["succeeded"]!.ToJsonString());

        var failed = data["failed"]!.AsArray();
        Assert.Single(failed);
        Assert.Equal("b2", failed[0]!["ref"]!.GetValue<string>());
    }

    [Fact]
    public async Task Parallel_all_tolerates_optional_branch_failure()
    {
        var dsl = ParallelDsl(
            "all",
            $"[ {BranchJson("b1", OkBranchBody)}, {BranchJson("b2", FailBranchBody, optional: true)} ]"
        );

        var result = await RunAsync(dsl, """{ "orderId": "ORD-1" }""");

        var data = Parse(result.OutputDataJson);
        Assert.Equal("false", data["ok"]!.ToJsonString());
        Assert.Equal("1", data["succeeded"]!.ToJsonString());
    }

    [Fact]
    public async Task Parallel_any_succeeds_on_first_success()
    {
        var result = await RunAsync(
            ParallelDsl(
                "any",
                $"[ {BranchJson("bad", FailBranchBody)}, {BranchJson("good", OkBranchBody)} ]",
                firstPath: "results.fan.good"
            ),
            """{ "orderId": "ORD-1" }"""
        );

        var data = Parse(result.OutputDataJson);
        Assert.Equal("1", data["succeeded"]!.ToJsonString());
        Assert.Equal("ORD-1", data["first"]!["orderId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Parallel_any_fails_when_all_branches_fail()
    {
        var result = await RunRawAsync(
            ParallelDsl(
                "any",
                $"[ {BranchJson("bad1", FailBranchBody)}, {BranchJson("bad2", FailBranchBody)} ]"
            ),
            """{ "orderId": "ORD-1" }"""
        );

        Assert.False(result.Succeeded);
        Assert.Contains("全部分支失败", result.Error, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // 校验：批量资源节点（R4）与写重试开关（R3）
    // ------------------------------------------------------------------

    [Fact]
    public void Batch_resource_node_without_items_input_is_rejected()
    {
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                {
                  "id": "b", "type": "ResourceBatchDelete",
                  "inputs": [{ "name": "resourceCode", "from": { "literal": "customer" } }]
                },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "b" },
                { "source": "b", "target": "end" }
              ]
            }
            """;

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("items", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Batch_resource_node_with_items_input_passes_validation()
    {
        const string dsl =
            """
            {
              "outputs": [
                { "name": "affected", "type": "number", "from": "affected", "visibleTo": { "mode": "all" } }
              ],
              "nodes": [
                { "id": "start", "type": "Start" },
                {
                  "id": "b", "type": "ResourceBatchDelete",
                  "inputs": [
                    { "name": "resourceCode", "from": { "literal": "customer" } },
                    { "name": "items", "from": "input.ids" }
                  ]
                },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "b" },
                { "source": "b", "target": "end" }
              ]
            }
            """;

        FlowExecutor.ValidateDsl(ParseDsl(dsl));
    }

    [Fact]
    public void Retry_on_write_without_idempotency_key_is_rejected()
    {
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                {
                  "id": "c", "type": "ResourceCreate", "retryOnWrite": true,
                  "inputs": [{ "name": "resourceCode", "from": { "literal": "customer" } }]
                },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "c" },
                { "source": "c", "target": "end" }
              ]
            }
            """;

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("幂等键", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Retry_on_write_with_idempotency_key_passes_validation()
    {
        const string dsl =
            """
            {
              "outputs": [
                { "name": "id", "type": "string", "from": "id", "visibleTo": { "mode": "all" } }
              ],
              "nodes": [
                { "id": "start", "type": "Start" },
                {
                  "id": "c", "type": "ResourceCreate", "retryOnWrite": true,
                  "inputs": [
                    { "name": "resourceCode", "from": { "literal": "customer" } },
                    { "name": "idempotencyKey", "from": "input.requestId" }
                  ]
                },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "c" },
                { "source": "c", "target": "end" }
              ]
            }
            """;

        FlowExecutor.ValidateDsl(ParseDsl(dsl));
    }

    [Fact]
    public void Retry_on_write_on_non_write_node_is_rejected()
    {
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "q", "type": "ResourceQuery", "retryOnWrite": true,
                  "inputs": [{ "name": "resourceCode", "from": { "literal": "customer" } }] },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "q" },
                { "source": "q", "target": "end" }
              ]
            }
            """;

        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
        Assert.Contains("不支持 retryOnWrite", ex.Message, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // 种子守卫：已发布演示流程必须始终能通过校验
    // ------------------------------------------------------------------

    [Theory]
    [InlineData("order-http-demo")]
    [InlineData("cf-foreach-demo")]
    [InlineData("cf-foreach-subflow-demo")]
    [InlineData("cf-parallel-demo")]
    public void Seeded_flows_pass_dsl_validation(string which)
    {
        var json = which switch
        {
            "order-http-demo" => FlowDefinitionDataSeedContributor.BuildDemoDslJson(),
            "cf-foreach-demo" => ControlFlowDemoSeedContributor.BuildForEachDsl(),
            "cf-foreach-subflow-demo" => ControlFlowDemoSeedContributor.BuildForEachSubFlowDsl(),
            "cf-parallel-demo" => ControlFlowDemoSeedContributor.BuildParallelDsl(),
            _ => throw new ArgumentOutOfRangeException(nameof(which))
        };

        FlowExecutor.ValidateDsl(ParseDsl(json));
    }

    [Fact]
    public async Task Seeded_foreach_demo_runs_end_to_end()
    {
        var result = await RunAsync(
            ControlFlowDemoSeedContributor.BuildForEachDsl(),
            """{ "lines": [ { "sku": "A", "qty": 2 }, { "sku": "B", "qty": 1 } ] }"""
        );

        var data = Parse(result.OutputDataJson);
        Assert.Equal("2", data["count"]!.ToJsonString());
        Assert.Equal("2", data["succeeded"]!.ToJsonString());
        Assert.Equal("A", data["items"]!.AsArray()[0]!["sku"]!.GetValue<string>());
    }

    [Fact]
    public async Task Seeded_parallel_demo_runs_end_to_end()
    {
        var result = await RunAsync(
            ControlFlowDemoSeedContributor.BuildParallelDsl(),
            """{ "orderId": "ORD-9", "amount": 5000 }"""
        );

        var data = Parse(result.OutputDataJson);
        Assert.Equal("true", data["ok"]!.ToJsonString());
        Assert.Equal("3", data["succeeded"]!.ToJsonString());
        Assert.Equal("high", data["risk"]!["riskLevel"]!.GetValue<string>());
        Assert.Equal("verified", data["basic"]!["status"]!.GetValue<string>());
    }

    // ------------------------------------------------------------------
    // 脚手架
    // ------------------------------------------------------------------

    private static readonly System.Text.Json.JsonSerializerOptions DslJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    /// <summary>把 DSL 文本反序列化成 <see cref="FlowDslDocument"/>（与运行时同一口径）。</summary>
    private static FlowDslDocument ParseDsl(string json)
    {
        return System.Text.Json.JsonSerializer.Deserialize<FlowDslDocument>(json, DslJsonOptions)
               ?? throw new InvalidOperationException("DSL 反序列化失败");
    }

    private static JsonObject Parse(string? json)
    {
        Assert.NotNull(json);
        var node = JsonNode.Parse(json);
        Assert.NotNull(node);
        return node.AsObject();
    }

    private static async Task<FlowExecutionResult> RunRawAsync(string dslJson, string? bodyJson)
    {
        var executor = CreateExecutor();
        return await executor.ExecuteAsync(dslJson, bodyJson, isDryRun: false);
    }

    private static async Task<FlowExecutionResult> RunAsync(string dslJson, string? bodyJson)
    {
        var result = await RunRawAsync(dslJson, bodyJson);
        Assert.True(result.Succeeded, result.Error);
        return result;
    }

    /// <summary>
    /// 只构造执行控制流所必需的依赖：SystemContextBuilder 用 stub 用户/租户；
    /// 其余（Http / SQL / Code / Rabbit / 资源 CRUD / 已发布流程缓存）在本组用例的节点集合中不会被触达。
    /// </summary>
    private static FlowExecutor CreateExecutor()
    {
        return new FlowExecutor(
            httpClientFactory: null!,
            systemContextBuilder: new SystemContextBuilder(new StubCurrentUser(), new StubCurrentTenant()),
            permissionChecker: null!,
            dataSourceResolver: null!,
            codeSandboxExecutor: null!,
            rabbitPublisher: null!,
            publishedFlowCache: null!,
            resourceCrudExecutor: null!,
            logger: NullLogger<FlowExecutor>.Instance
        );
    }

    private static string ForeachDsl(
        string itemErrorMode,
        string loopBody,
        int concurrency = 1,
        int maxIterations = 100,
        string inputType = "array"
    )
    {
        return """
{
  "version": "1.1",
  "key": "t-foreach",
  "inputs": [
    { "name": "lines", "type": "__INTYPE__", "required": true, "source": "input" }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "each",
      "type": "ForEach",
      "ref": "each",
      "itemsPath": "input.lines",
      "itemVar": "item",
      "indexVar": "i",
      "itemErrorMode": "__MODE__",
      "concurrency": __CONC__,
      "maxIterations": __MAX__,
      "loopBody": __BODY__,
      "outputs": [
        { "name": "items", "type": "array", "from": "items" },
        { "name": "count", "type": "number", "from": "count" },
        { "name": "succeeded", "type": "number", "from": "succeeded" },
        { "name": "failed", "type": "array", "from": "failed" }
      ]
    },
    {
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "items", "type": "array", "from": "items", "visibleTo": { "mode": "all" } },
        { "name": "count", "type": "number", "from": "count", "visibleTo": { "mode": "all" } },
        { "name": "succeeded", "type": "number", "from": "succeeded", "visibleTo": { "mode": "all" } },
        { "name": "failed", "type": "array", "from": "failed", "visibleTo": { "mode": "all" } }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "each" },
    { "source": "each", "target": "end" }
  ]
}
"""
            .Replace("__MODE__", itemErrorMode, StringComparison.Ordinal)
            .Replace("__CONC__", concurrency.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__MAX__", maxIterations.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("__INTYPE__", inputType, StringComparison.Ordinal)
            .Replace("__BODY__", loopBody, StringComparison.Ordinal);
    }

    private static string ParallelDsl(
        string joinMode,
        string branchesJson,
        string firstPath = "results.fan.b1"
    )
    {
        return """
{
  "version": "1.1",
  "key": "t-parallel",
  "inputs": [
    { "name": "orderId", "type": "string", "required": true, "source": "input" }
  ],
  "nodes": [
    { "id": "start", "type": "Start" },
    {
      "id": "fan",
      "type": "Parallel",
      "ref": "fan",
      "joinMode": "__MODE__",
      "inputs": [{ "name": "orderId", "from": "input.orderId" }],
      "branches": __BRANCHES__,
      "outputs": [
        { "name": "ok", "type": "boolean", "from": "ok" },
        { "name": "succeeded", "type": "number", "from": "succeeded" },
        { "name": "failed", "type": "array", "from": "failed" }
      ]
    },
    {
      "id": "end",
      "type": "End",
      "finalOutputs": [
        { "name": "ok", "type": "boolean", "from": "ok", "visibleTo": { "mode": "all" } },
        { "name": "succeeded", "type": "number", "from": "succeeded", "visibleTo": { "mode": "all" } },
        { "name": "failed", "type": "array", "from": "failed", "visibleTo": { "mode": "all" } },
        { "name": "first", "type": "object", "from": "__FIRST__", "visibleTo": { "mode": "all" } }
      ]
    }
  ],
  "edges": [
    { "source": "start", "target": "fan" },
    { "source": "fan", "target": "end" }
  ]
}
"""
            .Replace("__MODE__", joinMode, StringComparison.Ordinal)
            .Replace("__BRANCHES__", branchesJson, StringComparison.Ordinal)
            .Replace("__FIRST__", firstPath, StringComparison.Ordinal);
    }

    private static string BranchJson(string branchRef, string body, bool optional = false)
    {
        var optionalText = optional ? ", \"optional\": true" : "";
        return $"{{ \"ref\": \"{branchRef}\"{optionalText}, \"inputs\": [ {{ \"name\": \"orderId\", \"from\": \"input.orderId\" }} ], \"body\": {body} }}";
    }

    private const string OkLoopBody =
        """
        {
          "inputs": [
            { "name": "item", "type": "object", "source": "input" },
            { "name": "i", "type": "number", "source": "input" }
          ],
          "nodes": [
            { "id": "bs", "type": "Start" },
            {
              "id": "a",
              "type": "Assign",
              "ref": "a",
              "inputs": [
                { "name": "sku", "from": "input.item.sku" },
                { "name": "seq", "from": "input.i" }
              ],
              "outputs": [
                { "name": "sku", "from": "sku" },
                { "name": "seq", "from": "seq" }
              ]
            },
            {
              "id": "be",
              "type": "End",
              "finalOutputs": [
                { "name": "sku", "type": "string", "from": "sku", "visibleTo": { "mode": "all" } },
                { "name": "seq", "type": "number", "from": "seq", "visibleTo": { "mode": "all" } }
              ]
            }
          ],
          "edges": [
            { "source": "bs", "target": "a" },
            { "source": "a", "target": "be" }
          ]
        }
        """;

    /// <summary>当 item.fail 非空时抛出 —— 用于验证 failFast / continueOnError 两种失败策略。</summary>
    private const string ThrowLoopBody =
        """
        {
          "inputs": [
            { "name": "item", "type": "object", "source": "input" },
            { "name": "i", "type": "number", "source": "input" }
          ],
          "nodes": [
            { "id": "bs", "type": "Start" },
            {
              "id": "t",
              "type": "Throw",
              "ref": "t",
              "failItems": [
                { "no": 1, "left": "input.item.fail", "op": "isnotempty" }
              ],
              "failCombine": "1",
              "failCode": "Test:BadItem",
              "failMessage": "bad item at {{input.i}}"
            },
            {
              "id": "be",
              "type": "End",
              "finalOutputs": [
                { "name": "i", "type": "number", "from": "input.i", "visibleTo": { "mode": "all" } }
              ]
            }
          ],
          "edges": [
            { "source": "bs", "target": "t" },
            { "source": "t", "target": "be" }
          ]
        }
        """;

    private const string OkBranchBody =
        """
        {
          "inputs": [{ "name": "orderId", "type": "string", "source": "input" }],
          "nodes": [
            { "id": "s", "type": "Start" },
            {
              "id": "a",
              "type": "Assign",
              "ref": "a",
              "inputs": [{ "name": "orderId", "from": "input.orderId" }],
              "outputs": [{ "name": "orderId", "from": "orderId" }]
            },
            {
              "id": "e",
              "type": "End",
              "finalOutputs": [
                { "name": "orderId", "type": "string", "from": "orderId", "visibleTo": { "mode": "all" } }
              ]
            }
          ],
          "edges": [
            { "source": "s", "target": "a" },
            { "source": "a", "target": "e" }
          ]
        }
        """;

    /// <summary>无条件下失败的分支体（Throw 无 predicate 即必抛）。</summary>
    private const string FailBranchBody =
        """
        {
          "inputs": [],
          "nodes": [
            { "id": "s", "type": "Start" },
            { "id": "t", "type": "Throw", "ref": "t", "failCode": "Test:BranchBoom", "failMessage": "branch boom" },
            {
              "id": "e",
              "type": "End",
              "finalOutputs": [
                { "name": "branchFailed", "type": "boolean", "from": "branchFailed", "visibleTo": { "mode": "all" } }
              ]
            }
          ],
          "edges": [
            { "source": "s", "target": "t" },
            { "source": "t", "target": "e" }
          ]
        }
        """;

    private sealed class StubCurrentUser : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? Id => Guid.Parse("11111111-1111-1111-1111-111111111111");
        public string? UserName => "tester";
        public string? Name => "Tester";
        public string? SurName => null;
        public string? Email => "tester@example.com";
        public bool EmailVerified => true;
        public string? PhoneNumber => null;
        public bool PhoneNumberVerified => false;
        public Guid? TenantId => null;
        public string[] Roles => ["admin"];
        public System.Security.Claims.Claim? FindClaim(string claimType) => null;
        public System.Security.Claims.Claim[] FindClaims(string claimType) => [];
        public System.Security.Claims.Claim[] GetAllClaims() => [];
        public bool IsInRole(string roleName) => false;
    }

    private sealed class StubCurrentTenant : ICurrentTenant
    {
        public Guid? Id => null;
        public string? Name => null;
        public bool IsAvailable => true;
        public IDisposable Change(Guid? id, string? name = null) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose()
            {
                // 无状态 stub
            }
        }
    }
}
