using System.Text.Json;
using Meta.Dow.SaaS.Orchestration;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// Wait 节点（A4）与条件算子/控制流语义（A6）的发布期校验测试。
///
/// <para><b>A4 的动机</b>：契约文档承诺过 Wait 节点与 <c>asyncJobId</c> 出参，
/// 但引擎里既没有 dispatch 分支也没有字段——异步出参<b>永远不可消费</b>。
/// 这类"文档承诺了但代码没有"的缺口，测试是唯一的发现手段：
/// 若无测试把<code>wait</code> 钉在白名单上，它会在一次重构里被重新删掉。</para>
///
/// <para><b>A6 的动机</b>：<c>in</c> / <c>notIn</c> / <c>between</c> 等算子此前缺失，
/// 配了直接抛运行期异常；而 <c>strategy</c> 拼错会静默退化成"最后一条命中生效"，
/// 与命名完全相反。两类都必须提前到发布期。</para>
/// </summary>
public class WaitNodeAndConditionOpTests
{
    private static readonly JsonSerializerOptions DslJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static FlowDslDocument ParseDsl(string json) =>
        JsonSerializer.Deserialize<FlowDslDocument>(json, DslJsonOptions)
        ?? throw new InvalidOperationException("DSL 反序列化失败");

    private static Volo.Abp.UserFriendlyException Validate(string dsl) =>
        Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));

    private static string WaitDsl(string waitNode) =>
        $$"""
        {
          "outputs": [
            { "name": "body", "type": "string", "from": "body", "visibleTo": { "mode": "all" } }
          ],
          "nodes": [
            { "id": "start", "type": "Start" },
            { "id": "call", "type": "Http", "method": "POST", "url": "https://example.com/a", "async": true },
            {{waitNode}},
            { "id": "end", "type": "End" }
          ],
          "edges": [
            { "source": "start", "target": "call" },
            { "source": "call", "target": "wait" },
            { "source": "wait", "target": "end" }
          ]
        }
        """;

    private const string ValidWaitNode =
        """{ "id": "wait", "type": "Wait", "jobId": "{{results.call.jobId}}", "waitTimeoutMs": 5000 }""";

    // ==================================================================
    // A4：Wait 节点
    // ==================================================================

    [Fact]
    public void Wait_node_with_job_id_is_accepted()
    {
        FlowExecutor.ValidateDsl(ParseDsl(WaitDsl(ValidWaitNode)));
    }

    [Fact]
    public void Wait_node_accepts_job_id_from_inputs()
    {
        // 「从 Http 节点 outputs 透传 jobId」是常见画法，节点上可以不重复写 jobId。
        FlowExecutor.ValidateDsl(ParseDsl(WaitDsl(
            """{ "id": "wait", "type": "Wait", "inputs": [{ "name": "jobId", "from": "results.call.jobId" }] }""")));
    }

    [Fact]
    public void Wait_node_without_any_job_id_source_is_rejected()
    {
        var ex = Validate(WaitDsl("""{ "id": "wait", "type": "Wait", "waitTimeoutMs": 5000 }"""));

        Assert.Contains("jobId", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Wait_node_with_out_of_range_timeout_is_rejected(int timeoutMs)
    {
        // Wait 在主链上同步阻塞：0/负数会立即假超时，极大值会把线程占死。
        var ex = Validate(WaitDsl(
            $$"""{ "id": "wait", "type": "Wait", "jobId": "x", "waitTimeoutMs": {{timeoutMs}} }"""));

        Assert.Contains("waitTimeoutMs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wait_node_with_fail_on_timeout_false_and_no_outputs_is_rejected()
    {
        // 放行等待失败却不给下游任何判断依据 ⇒ 这个节点没有意义，多半是配反了。
        var ex = Validate(WaitDsl(
            """{ "id": "wait", "type": "Wait", "jobId": "x", "waitFailOnTimeout": false }"""));

        Assert.Contains("outputs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wait_node_with_fail_on_timeout_false_and_outputs_is_accepted()
    {
        FlowExecutor.ValidateDsl(ParseDsl(WaitDsl(
            """
            {
              "id": "wait", "type": "Wait", "jobId": "x", "waitFailOnTimeout": false,
              "outputs": [{ "name": "body", "from": "body" }]
            }
            """)));
    }

    [Fact]
    public void Wait_max_timeout_is_exposed_as_a_named_constant()
    {
        // 上界被单测钉住：日后若有人放宽，需连带更新测试与文档，而不是随手改魔法数字。
        Assert.Equal(10 * 60 * 1000, FlowExecutor.MaxWaitTimeoutMs);
    }

    // ==================================================================
    // A6：条件算子白名单
    // ==================================================================

    private static string ConditionDsl(string op, string right = "1", string? strategy = null) =>
        $$"""
        {
          "outputs": [
            { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "all" } }
          ],
          "inputs": [ { "name": "x", "source": "system", "systemKey": "userId" } ],
          "nodes": [
            { "id": "start", "type": "Start" },
            {
              "id": "c", "type": "Condition"{{(strategy == null ? "" : $", \"strategy\": \"{strategy}\"")}},
              "items": [{ "no": 1, "left": "input.x", "op": "{{op}}", "right": {{right}} }]
            },
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

    [Theory]
    [InlineData("eq")]
    [InlineData("==")]
    [InlineData("ne")]
    [InlineData("!=")]
    [InlineData("gt")]
    [InlineData(">")]
    [InlineData("gte")]
    [InlineData(">=")]
    [InlineData("lt")]
    [InlineData("<")]
    [InlineData("lte")]
    [InlineData("<=")]
    [InlineData("contains")]
    [InlineData("notcontains")]
    [InlineData("startswith")]   // A6 新增
    [InlineData("endswith")]     // A6 新增
    [InlineData("in")]           // A6 新增
    [InlineData("notin")]        // A6 新增
    [InlineData("between")]      // A6 新增
    [InlineData("isempty")]
    [InlineData("isnotempty")]
    [InlineData("isnull")]       // A6 新增
    [InlineData("isnotnull")]    // A6 新增
    public void Supported_condition_op_is_accepted(string op)
    {
        FlowExecutor.ValidateDsl(ParseDsl(ConditionDsl(op)));
    }

    [Theory]
    [InlineData("equals")]      // 近拼写，最容易踩
    [InlineData("startWith")]
    [InlineData("include")]
    [InlineData("regex")]
    public void Unsupported_condition_op_is_rejected_at_publish(string op)
    {
        // 原先只在运行期抛 Unsupported condition op：错误现场离配置现场很远。
        var ex = Validate(ConditionDsl(op));

        Assert.Contains("算子", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Condition_op_is_trimmed_and_case_insensitive()
    {
        // 大小写与首尾空白不该成为失败原因——配置手写常带这些噪声，
        // 让它们报错只会把发布者推向"照抄错误提示"的机械修法。
        FlowExecutor.ValidateDsl(ParseDsl(ConditionDsl("IN ")));
        FlowExecutor.ValidateDsl(ParseDsl(ConditionDsl("StartsWith")));
    }

    [Fact]
    public void Unsupported_op_in_fail_items_is_also_rejected()
    {
        // failItems 走同一套算子，不能只校验 items。
        const string dsl =
            """
            {
              "outputs": [
                { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "all" } }
              ],
              "nodes": [
                { "id": "start", "type": "Start" },
                {
                  "id": "h", "type": "Http", "url": "https://example.com",
                  "failItems": [{ "no": 1, "left": "input.x", "op": "equals", "right": 1 }]
                },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "h" },
                { "source": "h", "target": "end" }
              ]
            }
            """;

        var ex = Validate(dsl);
        Assert.Contains("算子", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Supported_condition_op_set_matches_documented_list()
    {
        // 白名单被单测钉住，避免"文档列了但引擎不支持"这类双向漂移。
        Assert.Equal(23, FlowExecutor.SupportedConditionOps.Count);
        foreach (var op in new[] { "startswith", "endswith", "in", "notin", "between", "isnull", "isnotnull" })
        {
            Assert.Contains(op, FlowExecutor.SupportedConditionOps);
        }
    }

    // ==================================================================
    // A6：Condition strategy 与 entry 语义
    // ==================================================================

    [Theory]
    [InlineData("firstMatch")]
    [InlineData("allMatch")]
    [InlineData("ALLMATCH")]      // 大小写不敏感
    [InlineData("  firstMatch  ")] // 首尾空白被容忍
    public void Valid_condition_strategy_is_accepted(string strategy)
    {
        FlowExecutor.ValidateDsl(ParseDsl(ConditionDsl("eq", strategy: strategy)));
    }

    [Theory]
    [InlineData("anyMatch")]      // 拼错：这是最可能写出来的形式
    [InlineData("lastMatch")]     // 拼错：恰好描述了旧实现的错误行为
    [InlineData("exclusive")]     // Mask 的取值，误用到 Condition
    public void Invalid_condition_strategy_is_rejected(string strategy)
    {
        // 原先从不校验 strategy，拼错会静默退化成"最后一条命中生效"——与命名完全相反。
        var ex = Validate(ConditionDsl("eq", strategy: strategy));

        Assert.Contains("strategy", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Edge_combine_without_items_is_rejected()
    {
        // 原先掉进 legacy 分支把 combine 完全忽略并静默走第一条边。
        const string dsl =
            """
            {
              "outputs": [
                { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "all" } }
              ],
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "c", "type": "Condition" },
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

        var ex = Validate(dsl);
        Assert.Contains("items", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Entry_combine_without_items_is_rejected()
    {
        // entry 只配 combine、items 为空 ⇒ 运行期直接 return true（恒真）。
        const string dsl =
            """
            {
              "outputs": [
                { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "all" } }
              ],
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "h", "type": "Http", "url": "https://example.com", "entry": { "combine": "1" } },
                { "id": "end", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "h" },
                { "source": "h", "target": "end" }
              ]
            }
            """;

        var ex = Validate(dsl);
        Assert.Contains("恒为真", ex.Message, StringComparison.Ordinal);
    }
}