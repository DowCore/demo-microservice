using System;
using System.Text.Json;
using Meta.Dow.SaaS.Orchestration;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// DSL 发布前校验的收紧规则（评审 P0-1 / P0-2 / P0-3 + C13）。
///
/// 只调用纯静态的 <see cref="FlowExecutor.ValidateDsl"/> 与
/// <see cref="NodeResiliencePolicy.IsTransientPostgresSqlState"/>，不需要构造执行器/数据库。
///
/// 三组规则的动机（见 docs/detail/logic-orchestration-review.md §4）：
/// 1. **P0-1 节点类型白名单（C2）**：dispatch 对未知类型抛 500，此前"发布成功、调用即 500"。
///    现在把这类错误提前到发布期。特别地 <c>Sql</c>/<c>Switch</c> 引擎没有分支，必须拒；
///    <c>Wait</c> 已由 A4 补齐分支并纳入名单，但其"缺 jobId"类配置错误同样在发布期拦。
/// 2. **P0-2 输出契约强制（S7）**：未声明契约时引擎曾回退输出整个 <c>ctx.Vars</c>（内部变量整包外泄）。
///    现在根级 <c>outputs</c> 或**每个** End 的 <c>finalOutputs</c> 至少声明其一，否则拒绝发布。
/// 3. **P0-3 可见性取值收敛（S12）**：<c>visibleTo.mode</c> 拼错曾兜底放行（等于公开）；
///    <c>roleIds</c> 与角色名比较恒不匹配（等于静默失效）。两者现在都在发布期报错。
/// </summary>
public class FlowExecutorDslValidationTests
{
    private static readonly JsonSerializerOptions DslJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static FlowDslDocument ParseDsl(string json)
    {
        return JsonSerializer.Deserialize<FlowDslDocument>(json, DslJsonOptions)
               ?? throw new InvalidOperationException("DSL 反序列化失败");
    }

    private static Volo.Abp.UserFriendlyException Validate(string dsl)
    {
        return Assert.Throws<Volo.Abp.UserFriendlyException>(() => FlowExecutor.ValidateDsl(ParseDsl(dsl)));
    }

    // ==================================================================
    // P0-1 节点类型白名单
    // ==================================================================

    private static string SingleNodeDsl(string type) =>
        $$"""
        {
          "outputs": [
            { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "all" } }
          ],
          "nodes": [
            { "id": "start", "type": "Start" },
            { "id": "x", "type": "{{type}}" },
            { "id": "end", "type": "End" }
          ],
          "edges": [
            { "source": "start", "target": "x" },
            { "source": "x", "target": "end" }
          ]
        }
        """;

    [Theory]
    [InlineData("Sql")] // 引擎无 "sql" dispatch 分支
    [InlineData("Switch")] // 引擎无 "switch" 分支（多路分支须用 Condition）
    [InlineData("HttppCall")] // 拼错类型
    public void Unsupported_node_type_is_rejected_at_publish(string type)
    {
        var ex = Validate(SingleNodeDsl(type));

        Assert.Contains("不受支持", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Whitelist_excludes_types_without_dispatch_branch()
    {
        // 这两个类型是评审 C2/E1/E2 的核心：曾"发布成功、调用即 500"。
        Assert.DoesNotContain("sql", FlowExecutor.SupportedNodeKinds);
        Assert.DoesNotContain("switch", FlowExecutor.SupportedNodeKinds);
    }

    [Fact]
    public void Whitelist_now_includes_wait()
    {
        // A4 补齐了 wait 的 dispatch 分支，wait 从"白名单拒绝"转为"可用但必须配 jobId"。
        Assert.Contains("wait", FlowExecutor.SupportedNodeKinds);
    }

    [Fact]
    public void Whitelist_covers_control_flow_and_resource_kinds()
    {
        // 控制流原语 + R4 批量入口都必须在名单内，否则会被自家白名单误杀。
        foreach (var kind in new[]
                 {
                     "start", "end", "condition", "foreach", "loop", "parallel", "when", "subflow",
                     "resourcequery", "resourcecreate", "resourceupdate", "resourcedelete",
                     "resourcebatchcreate", "resourcebatchupdate", "resourcebatchdelete"
                 })
        {
            Assert.Contains(kind, FlowExecutor.SupportedNodeKinds);
        }
    }

    [Fact]
    public void Supported_node_type_passes_validation()
    {
        FlowExecutor.ValidateDsl(ParseDsl(SingleNodeDsl("Log")));
    }

    // ==================================================================
    // P0-2 输出契约强制
    // ==================================================================

    private const string NoContractDsl =
        """
        {
          "nodes": [
            { "id": "start", "type": "Start" },
            { "id": "l", "type": "Log", "message": "hi" },
            { "id": "end", "type": "End" }
          ],
          "edges": [
            { "source": "start", "target": "l" },
            { "source": "l", "target": "end" }
          ]
        }
        """;

    private const string RootOutputsDsl =
        """
        {
          "outputs": [
            { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "all" } }
          ],
          "nodes": [
            { "id": "start", "type": "Start" },
            { "id": "l", "type": "Log", "message": "hi" },
            { "id": "end", "type": "End" }
          ],
          "edges": [
            { "source": "start", "target": "l" },
            { "source": "l", "target": "end" }
          ]
        }
        """;

    private const string EndFinalOutputsDsl =
        """
        {
          "nodes": [
            { "id": "start", "type": "Start" },
            { "id": "l", "type": "Log", "message": "hi" },
            {
              "id": "end",
              "type": "End",
              "finalOutputs": [
                { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "all" } }
              ]
            }
          ],
          "edges": [
            { "source": "start", "target": "l" },
            { "source": "l", "target": "end" }
          ]
        }
        """;

    [Fact]
    public void Flow_without_output_contract_is_rejected_at_publish()
    {
        var ex = Validate(NoContractDsl);

        Assert.Contains("输出契约", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Flow_with_root_outputs_passes_validation()
    {
        FlowExecutor.ValidateDsl(ParseDsl(RootOutputsDsl));
    }

    [Fact]
    public void Flow_with_end_final_outputs_passes_validation()
    {
        FlowExecutor.ValidateDsl(ParseDsl(EndFinalOutputsDsl));
    }

    [Fact]
    public void Flow_with_one_end_lacking_final_outputs_is_rejected()
    {
        // 两个 End：一个声明了契约、一个没声明 —— 无根级 outputs 时必须"每个" End 都声明。
        // 用 Condition 分叉（非 Condition 节点不允许一条以上出边）。
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "c", "type": "Condition", "items": [{ "no": 1, "left": "input.x", "op": "gt", "right": 1 }] },
                {
                  "id": "e1",
                  "type": "End",
                  "finalOutputs": [
                    { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "all" } }
                  ]
                },
                { "id": "e2", "type": "End" }
              ],
              "edges": [
                { "source": "start", "target": "c" },
                { "source": "c", "target": "e1", "combine": "1" },
                { "source": "c", "target": "e2", "isDefault": true }
              ]
            }
            """;

        var ex = Validate(dsl);

        Assert.Contains("输出契约", ex.Message, StringComparison.Ordinal);
    }

    // ==================================================================
    // P0-3 visibleTo 取值收敛
    //
    // 校验入口是发布期的 FlowDefinitionAppService.ValidateOutputContracts，
    // 不是 FlowExecutor.ValidateDsl：可见性错配必须在"发布"这个动作上就失败，
    // 留到运行期只是把错误推迟到用户请求时暴露。ValidateOutputContract 本身
    // 只保留"是否声明了输出契约"的前置检查（因为它被 DSL 反序列化后的其他路径共用）。
    // ==================================================================

    /// <summary>visibleTo 的校验入口：与 PublishAsync 走同一条路径。</summary>
    private static Volo.Abp.UserFriendlyException ValidateContract(string dsl) =>
        Assert.Throws<Volo.Abp.UserFriendlyException>(
            () => FlowDefinitionAppService.ValidateOutputContractsForTest(ParseDsl(dsl)));

    private static string VisibleToDsl(string visibleToJson) =>
        $$"""
        {
          "outputs": [
            { "name": "x", "type": "string", "from": "x", "visibleTo": {{visibleToJson}} }
          ],
          "nodes": [
            { "id": "start", "type": "Start" },
            { "id": "l", "type": "Log", "message": "hi" },
            { "id": "end", "type": "End" }
          ],
          "edges": [
            { "source": "start", "target": "l" },
            { "source": "l", "target": "end" }
          ]
        }
        """;

    [Theory]
    [InlineData("all")]
    [InlineData("none")]
    [InlineData("roles")]
    [InlineData("permissions")]
    public void Known_visible_to_mode_passes_validation(string mode)
    {
        FlowDefinitionAppService.ValidateOutputContractsForTest(
            ParseDsl(VisibleToDsl($"{{ \"mode\": \"{mode}\" }}")));
    }

    [Fact]
    public void Unknown_visible_to_mode_is_rejected()
    {
        // 此前未知 mode 会走到 `return true` 兜底 = 字段对所有角色公开。
        var ex = ValidateContract(VisibleToDsl("{ \"mode\": \"evryone\" }"));

        Assert.Contains("visibleTo.mode", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Role_ids_in_visible_to_is_rejected()
    {
        // ICurrentUser 只暴露角色名，roleIds 与角色名比较恒不匹配 = 静默失效。
        var ex = ValidateContract(VisibleToDsl(
            "{ \"mode\": \"roles\", \"roleIds\": [\"11111111-1111-1111-1111-111111111111\"] }"));

        Assert.Contains("roleIds", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Visible_to_on_end_final_outputs_is_also_validated()
    {
        const string dsl =
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                {
                  "id": "end",
                  "type": "End",
                  "finalOutputs": [
                    { "name": "x", "type": "string", "from": "x", "visibleTo": { "mode": "bogus" } }
                  ]
                }
              ],
              "edges": [
                { "source": "start", "target": "end" }
              ]
            }
            """;

        var ex = ValidateContract(dsl);

        Assert.Contains("visibleTo.mode", ex.Message, StringComparison.Ordinal);
        Assert.Contains("finalOutputs", ex.Message, StringComparison.Ordinal);
    }

    // ==================================================================
    // C13 PostgreSQL SqlState 分类（曾走 int.TryParse，40P01/08xxx 恒不重试）
    // ==================================================================

    [Theory]
    [InlineData("40001", true)] // serialization_failure
    [InlineData("40P01", true)] // deadlock_detected —— 字母导致 TryParse 失败，旧的致命用例
    [InlineData("08006", true)] // connection_failure
    [InlineData("08003", true)] // connection_does_not_exist
    [InlineData("53300", true)] // too_many_connections
    [InlineData("55P03", true)] // lock_not_available
    [InlineData("57P01", true)] // admin_shutdown
    [InlineData("23505", false)] // unique_violation（业务错，不可重试）
    [InlineData("42P01", false)] // undefined_table
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Postgres_sqlstate_classification(string? sqlState, bool expected)
    {
        Assert.Equal(expected, NodeResiliencePolicy.IsTransientPostgresSqlState(sqlState));
    }
}
