using System.Collections.Generic;
using Meta.Dow.SaaS.Orchestration;
using Volo.Abp;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// 审批提交时的字段权限过滤（W8）。核心回归点：只读/隐藏字段拒绝写入、未知权限值失败关闭。
/// </summary>
public class WorkflowRecordPatchTests
{
    private static Dictionary<string, object?> Patch() =>
        new()
        {
            ["amount"] = 100,
            ["reason"] = "出差",
            ["secret"] = "x",
            ["typo"] = "y",
        };

    [Fact]
    public void Read_and_hide_fields_are_ignored()
    {
        var permissions = new Dictionary<string, string>
        {
            ["amount"] = "read",
            ["secret"] = "hide",
        };

        var result = WorkflowRuntimeAppService.FilterRecordPatch(permissions, Patch());

        Assert.False(result.ContainsKey("amount"));
        Assert.False(result.ContainsKey("secret"));
        Assert.True(result.ContainsKey("reason"));
        Assert.True(result.ContainsKey("typo"));
    }

    [Fact]
    public void Write_and_required_fields_are_allowed()
    {
        var permissions = new Dictionary<string, string>
        {
            ["amount"] = "write",
            ["reason"] = "required",
        };

        var result = WorkflowRuntimeAppService.FilterRecordPatch(permissions, Patch());

        Assert.True(result.ContainsKey("amount"));
        Assert.True(result.ContainsKey("reason"));
    }

    [Fact]
    public void Unknown_permission_value_fails_closed()
    {
        var permissions = new Dictionary<string, string> { ["amount"] = "reade" };

        Assert.Throws<UserFriendlyException>(() => WorkflowRuntimeAppService.FilterRecordPatch(permissions, Patch()));
    }

    [Fact]
    public void Fields_without_permission_config_are_allowed()
    {
        var result = WorkflowRuntimeAppService.FilterRecordPatch(null, Patch());
        Assert.Equal(4, result.Count);
    }
}
