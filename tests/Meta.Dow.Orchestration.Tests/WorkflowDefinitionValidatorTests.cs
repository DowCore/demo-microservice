using System;
using System.Collections.Generic;
using System.Linq;
using Meta.Dow.SaaS.Orchestration;
using Volo.Abp;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// OA 流程定义发布校验（W1/W2/W3/W6/W7）。这些规则把配置错误拦在发布口，避免运行时爆发。
/// </summary>
public class WorkflowDefinitionValidatorTests
{
    private const string ValidGraph =
        """
        {
          "nodes": [
            { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start", "name": "开始" } },
            { "id": "a1", "type": "approver", "data": { "nodeId": "a1", "type": "approver", "name": "审批", "assigneeType": "role", "assigneeValue": "manager" } },
            { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end", "name": "结束" } }
          ],
          "edges": [
            { "source": "start", "target": "a1", "data": {} },
            { "source": "a1", "target": "end", "data": {} }
          ]
        }
        """;

    [Fact]
    public void Valid_graph_passes()
    {
        var exception = Record.Exception(() => WorkflowDefinitionValidator.Validate(ValidGraph, ["amount", "reason"]));
        Assert.Null(exception);
    }

    [Fact]
    public void Missing_end_node_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "a1", "type": "approver", "data": { "nodeId": "a1", "type": "approver", "assigneeType": "role", "assigneeValue": "r" } }
              ],
              "edges": [ { "source": "start", "target": "a1", "data": {} } ]
            }
            """;

        Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, null));
    }

    [Fact]
    public void Duplicate_node_id_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "start", "type": "approver", "data": { "nodeId": "start", "type": "approver" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } }
              ],
              "edges": [
                { "source": "start", "target": "end", "data": {} }
              ]
            }
            """;

        var ex = Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, null));
        Assert.Contains("重复", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Edge_to_missing_target_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } }
              ],
              "edges": [ { "source": "start", "target": "ghost", "data": {} } ]
            }
            """;

        var ex = Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, null));
        Assert.Contains("不存在的节点", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Node_without_outgoing_edge_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } },
                { "id": "dangling", "type": "approver", "data": { "nodeId": "dangling", "type": "approver", "assigneeType": "role", "assigneeValue": "r" } }
              ],
              "edges": [ { "source": "start", "target": "end", "data": {} } ]
            }
            """;

        Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, null));
    }

    [Fact]
    public void Approver_without_assignee_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "a1", "type": "approver", "data": { "nodeId": "a1", "type": "approver", "assigneeType": "role", "assigneeValue": "" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } }
              ],
              "edges": [
                { "source": "start", "target": "a1", "data": {} },
                { "source": "a1", "target": "end", "data": {} }
              ]
            }
            """;

        Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, null));
    }

    [Fact]
    public void Unknown_node_type_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "x", "type": "aprpver", "data": { "nodeId": "x", "type": "aprpver" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } }
              ],
              "edges": [
                { "source": "start", "target": "x", "data": {} },
                { "source": "x", "target": "end", "data": {} }
              ]
            }
            """;

        Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, null));
    }

    [Fact]
    public void Condition_field_not_in_form_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "a1", "type": "approver", "data": { "nodeId": "a1", "type": "approver", "assigneeType": "role", "assigneeValue": "r" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } }
              ],
              "edges": [
                { "source": "start", "target": "a1", "data": {} },
                { "source": "a1", "target": "end", "data": { "conditionField": "ghostField", "conditionOp": "eq", "conditionValue": "1" } }
              ]
            }
            """;

        var ex = Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, ["amount"]));
        Assert.Contains("ghostField", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_conditional_priority_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "a1", "type": "approver", "data": { "nodeId": "a1", "type": "approver", "assigneeType": "role", "assigneeValue": "r" } },
                { "id": "b1", "type": "approver", "data": { "nodeId": "b1", "type": "approver", "assigneeType": "role", "assigneeValue": "r" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } }
              ],
              "edges": [
                { "source": "start", "target": "a1", "data": { "conditionField": "amount", "conditionOp": "gt", "conditionValue": "100", "priority": 1 } },
                { "source": "start", "target": "b1", "data": { "conditionField": "amount", "conditionOp": "lte", "conditionValue": "100", "priority": 1 } },
                { "source": "a1", "target": "end", "data": {} },
                { "source": "b1", "target": "end", "data": {} }
              ]
            }
            """;

        var ex = Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, ["amount"]));
        Assert.Contains("优先级", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Distinct_conditional_priority_passes()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "a1", "type": "approver", "data": { "nodeId": "a1", "type": "approver", "assigneeType": "role", "assigneeValue": "r" } },
                { "id": "b1", "type": "approver", "data": { "nodeId": "b1", "type": "approver", "assigneeType": "role", "assigneeValue": "r" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } }
              ],
              "edges": [
                { "source": "start", "target": "a1", "data": { "conditionField": "amount", "conditionOp": "gt", "conditionValue": "100", "priority": 1 } },
                { "source": "start", "target": "b1", "data": { "conditionField": "amount", "conditionOp": "lte", "conditionValue": "100", "priority": 2 } },
                { "source": "a1", "target": "end", "data": {} },
                { "source": "b1", "target": "end", "data": {} }
              ]
            }
            """;

        var exception = Record.Exception(() => WorkflowDefinitionValidator.Validate(graph, ["amount"]));
        Assert.Null(exception);
    }

    [Fact]
    public void Cycle_fails()
    {
        const string graph =
            """
            {
              "nodes": [
                { "id": "start", "type": "start", "data": { "nodeId": "start", "type": "start" } },
                { "id": "a1", "type": "approver", "data": { "nodeId": "a1", "type": "approver", "assigneeType": "role", "assigneeValue": "r" } },
                { "id": "end", "type": "end", "data": { "nodeId": "end", "type": "end" } }
              ],
              "edges": [
                { "source": "start", "target": "a1", "data": {} },
                { "source": "a1", "target": "start", "data": {} }
              ]
            }
            """;

        Assert.Throws<UserFriendlyException>(() => WorkflowDefinitionValidator.Validate(graph, null));
    }
}
