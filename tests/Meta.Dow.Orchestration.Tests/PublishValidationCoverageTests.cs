using System;
using System.Linq;
using Meta.Dow.SaaS.Orchestration;
using Volo.Abp;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// 发布期校验的<b>覆盖范围</b>测试。
///
/// <para>这批测试针对的是一个反复出现的模式：校验逻辑本身写对了，但只遍历了
/// "最显眼的那一层"，于是嵌套分支与End 节点成为后门。本轮修的三处都是这个模式的实例——
/// <c>payload</c> 被误当成未实现字段（挡住合法配置）、
/// <c>aggregate</c>/<c>map</c> 冲突检查漏 <c>End.finalOutputs</c>、
/// <c>visibleTo</c> 漏嵌套字段。测试的意义就是钉住"覆盖范围"，防止再次退化成只查根级。</para>
/// </summary>
public class PublishValidationCoverageTests
{
    private static FlowDslDocument Dsl(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<FlowDslDocument>(
            json,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    // ==================================================================
    // 修复一：payload 不是"未实现字段"，而是已实现的字段
    // ==================================================================

    [Fact]
    public void Payload_field_is_accepted_because_it_is_consumed()
    {
        // BuildRabbitPayload 消费 node.Payload.Item 做消息体字段投影。
        // 曾经的回归：payload 被列进「模型有但无消费点」黑名单，导致该形态根本无法发布，
        // 而提示语却写着「仅支持 raw 与 payload.item」——自相矛盾。
        DslSchemaValidator.ValidateJson(
            """
            {
              "nodes": [
                { "id": "p", "type": "ResourceCreate",
                  "payload": { "item": [ { "name": "id", "from": "id" } ] } }
              ]
            }
            """);
    }

    [Theory]
    [InlineData("object")]
    [InlineData("raw")]
    [InlineData("RAW")]   //大小写容忍，与条件算子保持同一套容错策略
    [InlineData(" raw ")]
    public void Supported_payload_mode_values_are_accepted(string mode)
    {
        DslSchemaValidator.ValidateSemantics(Dsl(
            $$"""{ "nodes": [ { "id": "p", "type": "ResourceCreate", "payloadMode": "{{mode}}" } ] }"""));
    }

    [Fact]
    public void Absent_payload_mode_defaults_to_object_and_is_accepted()
    {
        DslSchemaValidator.ValidateSemantics(
            Dsl("""{ "nodes": [ { "id": "p", "type": "ResourceCreate" } ] }"""));
    }

    [Fact]
    public void Payload_mode_array_is_rejected_as_silently_degrading()
    {
        // 模型注释曾写「object | array | raw」，但 BuildRabbitPayload 根本没有 array 分支——
        // 配了它会静默退化成「整包 inputs」。这正是"配了等于没配"，必须拦。
        var ex = Assert.Throws<UserFriendlyException>(() => DslSchemaValidator.ValidateSemantics(Dsl(
            """{ "nodes": [ { "id": "p", "type": "ResourceCreate", "payloadMode": "array" } ] }""")));

        Assert.Contains("payloadMode", ex.Message, StringComparison.Ordinal);
        Assert.Contains("array", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_unsupported_payload_mode_is_reported_not_just_the_first()
    {
        var ex = Assert.Throws<UserFriendlyException>(() => DslSchemaValidator.ValidateSemantics(Dsl(
            """
            {
              "nodes": [
                { "id": "a", "type": "ResourceCreate", "payloadMode": "arr" },
                { "id": "b", "type": "ResourceCreate", "payloadMode": "raw2" }
              ]
            }
            """)));

        Assert.Contains("nodes.a.payloadMode", ex.Message, StringComparison.Ordinal);
        Assert.Contains("nodes.b.payloadMode", ex.Message, StringComparison.Ordinal);
    }

    // ==================================================================
    // 修复二 & 三：出参契约校验必须覆盖 End.finalOutputs 与嵌套字段
    // ==================================================================

    [Fact]
    public void Aggregate_map_conflict_is_detected_in_end_final_outputs()
    {
        // ResolveFinalSchema 是 FinalOutputs 优先，故只配 End 的流程同样受契约约束。
        // 曾经 ValidateOutputContracts 只 foreach(dsl.Outputs)，这类流程可完整绕过冲突拦截。
        var ex = Assert.Throws<UserFriendlyException>(() => FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
            """
            {
              "nodes": [
                { "id": "end", "type": "End",
                  "finalOutputs": [
                    { "name": "total", "from": "results.q.items",
                      "aggregate": { "op": "count" },
                      "map": { "item": [ { "name": "id", "from": "id" } ] } }
                  ] }
              ]
            }
            """)));

        Assert.Contains("aggregate", ex.Message, StringComparison.Ordinal);
        Assert.Contains("map.item", ex.Message, StringComparison.Ordinal);
        Assert.Contains("finalOutputs", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unsupported_wrap_is_detected_in_end_final_outputs()
    {
        var ex = Assert.Throws<UserFriendlyException>(() => FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
            """
            {
              "nodes": [
                { "id": "end", "type": "End",
                  "finalOutputs": [ { "name": "x", "from": "results.q.items", "wrap": "object" } ] }
              ]
            }
            """)));

        Assert.Contains("wrap", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_map_item_visible_to_is_validated()
    {
        // 嵌套 visibleTo 拼错时OutputVisibilityFilter.IsVisible 走 return true 兜底，
        // 结果是"配了可见性却全可见"——比不配更危险。
        var ex = Assert.Throws<UserFriendlyException>(
            () => FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
                """
                {
                  "outputs": [
                    { "name": "rows", "type": "array", "from": "results.q.items",
                      "map": { "item": [ { "name": "amount", "from": "amount", "visibleTo": { "mode": "rols" } } ] } }
                  ]
                }
                """)));

        Assert.Contains("rols", ex.Message, StringComparison.Ordinal);
        Assert.Contains("outputs.rows.map.item[0]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Deeply_nested_map_item_visible_to_is_validated()
    {
        // map.item 可无限层级；只查第一层等于给第二层留后门。
        var ex = Assert.Throws<UserFriendlyException>(
            () => FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
                """
                {
                  "outputs": [
                    { "name": "rows", "type": "array", "from": "results.q.items",
                      "map": { "item": [
                        { "name": "detail", "type": "object", "from": "detail",
                          "map": { "item": [ { "name": "secret", "from": "s", "visibleTo": { "roleIds": ["r1"] } } ] } }
                      ] } }
                  ]
                }
                """)));

        Assert.Contains("roleIds", ex.Message, StringComparison.Ordinal);
        Assert.Contains("outputs.rows.map.item[0].map.item[0]", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Group_header_and_children_visible_to_are_validated()
    {
        // 分组出参有两条独立的可见性入口，漏任一条都会导致分组数据整包外泄。
        var headerEx = Assert.Throws<UserFriendlyException>(
            () => FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
                """
                {
                  "outputs": [
                    { "name": "orders", "type": "array", "from": "results.q.items",
                      "group": {
                        "by": "orderId",
                        "header": [ { "name": "orderId", "from": "orderId", "visibleTo": { "mode": "nonee" } } ]
                      } }
                  ]
                }
                """)));
        Assert.Contains("group.header[0]", headerEx.Message, StringComparison.Ordinal);

        var childrenEx = Assert.Throws<UserFriendlyException>(
            () => FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
                """
                {
                  "outputs": [
                    { "name": "orders", "type": "array", "from": "results.q.items",
                      "group": {
                        "by": "orderId",
                        "children": { "name": "items", "visibleTo": { "mode": "permission" },
                                      "map": { "item": [ { "name": "id", "from": "id" } ] } }
                      } }
                  ]
                }
                """)));
        Assert.Contains("group.children", childrenEx.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_level_end_and_nested_valid_contracts_all_pass()
    {
        // 反向断言：修完之后不能变成"一律拒绝"。以下是三种合法载体各一条。
        FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
            """
            {
              "outputs": [
                { "name": "rows", "type": "array", "from": "results.q.items",
                  "map": { "item": [ { "name": "id", "from": "id", "visibleTo": { "mode": "roles", "roleNames": ["admin"] } } ] },
                  "group": {
                    "by": "orderId",
                    "header": [ { "name": "orderId", "from": "orderId", "visibleTo": { "mode": "all" } } ],
                    "children": { "name": "items", "visibleTo": { "mode": "all" },
                                  "map": { "item": [ { "name": "sku", "from": "sku" } ] } }
                  } }
              ],
              "nodes": [
                { "id": "end", "type": "End",
                  "finalOutputs": [ { "name": "count", "from": "results.q.items", "aggregate": { "op": "count" } } ] }
              ]
            }
            """));
    }

    [Fact]
    public void Contract_validation_ignores_non_end_nodes_final_outputs()
    {
        // 只有 Type=End 的节点才承载 finalOutputs；普通节点上写了不该被当成契约，
        // 否则同名校验会给出误导性的报错位置。
        FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
            """
            {
              "outputs": [],
              "nodes": [
                { "id": "q", "type": "ResourceQuery",
                  "finalOutputs": [ { "name": "x", "from": "items", "wrap": "object" } ] }
              ]
            }
            """));
    }

    [Fact]
    public void Validation_is_case_insensitive_on_end_node_type()
    {
        var ex = Assert.Throws<UserFriendlyException>(() => FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
            """
            {
              "nodes": [
                { "id": "end", "type": "end",
                  "finalOutputs": [ { "name": "x", "from": "results.q.items", "wrap": "objekt" } ] }
              ]
            }
            """)));

        Assert.Contains("wrap", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Null_nodes_list_does_not_throw()
    {
        // 反序列化出的空文档不该把发布流程炸掉——那属于反序列化层要报的问题。
        var dsl = new FlowDslDocument();
        dsl.Outputs = null!;

        FlowDefinitionAppService.ValidateOutputContractsForTest(dsl);
    }

    [Fact]
    public void Every_contract_problem_names_its_own_output_path()
    {
        // 报错必须能定位到具体出参，否则多出参流程里用户无从下手。
        var ex = Assert.Throws<UserFriendlyException>(() => FlowDefinitionAppService.ValidateOutputContractsForTest(Dsl(
            """
            {
              "nodes": [
                { "id": "done", "type": "End",
                  "finalOutputs": [
                    { "name": "a", "from": "results.q.items", "wrap": "objekt" },
                    { "name": "b", "from": "results.q.items", "wrap": "objekt" }
                  ] }
              ]
            }
            """)));

        Assert.Contains("finalOutputs.a", ex.Message, StringComparison.Ordinal);
    }
}