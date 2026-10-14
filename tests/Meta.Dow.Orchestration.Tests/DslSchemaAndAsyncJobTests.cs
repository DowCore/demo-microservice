using System;
using Meta.Dow.SaaS.Orchestration;
using Xunit;

namespace Meta.Dow.Orchestration.Tests;

/// <summary>
/// DSL 结构校验测试（A1）与异步作业状态机测试（A3）。
///
/// <para><b>A1 的动机</b>：<c>JsonSerializer</c> 默认丢弃无法映射的字段，
///于是 <c>txMode</c>、拼错的 <c>subFlowKeyy</c> 全部无声消失——配了等于没配。
///修复后未知字段在发布期报错，并且拼写接近时给出纠正建议。</para>
///
/// <para><b>A3 的动机</b>：异步外呼原是 <c>Task.Run</c> fire-and-forget，
/// 结果无人可取、失败只进日志、进程重启即丢。修复后作业成为持久化实体，
/// 状态流转必须严格，否则"排队中"与"已失败"会再度混同。</para>
/// </summary>
public class DslSchemaAndAsyncJobTests
{
    // ==================================================================
    // A1：未知字段
    // ==================================================================

    [Fact]
    public void Unknown_node_field_is_rejected()
    {
        // 拼错的 subFlowKeyy 是文档里出现过的真实笔误。
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => DslSchemaValidator.ValidateJson(
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                { "id": "s", "type": "SubFlow", "subFlowKeyy": "child" }
              ],
              "edges": [{ "source": "start", "target": "s" }]
            }
            """));

        Assert.Contains("subFlowKeyy", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_field_suggests_the_intended_spelling()
    {
        // 拼写建议把"用户盲猜"变成"一眼看出"，这是发布期校验能否被用起来的关键。
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => DslSchemaValidator.ValidateJson(
            """
            {
              "nodes": [
                { "id": "start", "type": "Start", "refr": "r" }
              ]
            }
            """));

        Assert.Contains("ref", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_root_field_is_rejected()
    {
        Assert.Throws<Volo.Abp.UserFriendlyException>(() => DslSchemaValidator.ValidateJson(
            """
            { "nodes": [], "edges": [], "txMode": "serializable" }
            """));
    }

    [Fact]
    public void Unknown_nested_field_is_reported_with_its_path()
    {
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => DslSchemaValidator.ValidateJson(
            """
            {
              "nodes": [
                { "id": "start", "type": "Start" },
                {
                  "id": "c", "type": "Condition",
                  "items": [{ "no": 1, "left": "input.x", "op": "eq", "right": 1, "opCase": "x" }]
                }
              ]
            }
            """));

        Assert.Contains("opCase", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Known_but_unimplemented_field_is_rejected_with_alternative()
    {
        // 「模型里有、但无消费点」的字段比未知字段更具欺骗性：用户会以为功能已生效。
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => DslSchemaValidator.ValidateJson(
            """
            { "nodes": [ { "id": "q", "type": "ResourceQuery", "transform": "{}" } ] }
            """));

        Assert.Contains("transform", ex.Message, StringComparison.Ordinal);
        Assert.Contains("尚未实现", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Unimplemented_field_hint_for_async_job_id_reflects_current_implementation()
    {
        // A3 已把JobId 落库，该提示文案必须随之更新，否则会给出过时的"尚未持久化"结论。
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => DslSchemaValidator.ValidateJson(
            """
            { "nodes": [ { "id": "h", "type": "Http", "asyncJobId": "x" } ] }
            """));

        Assert.Contains("尚未实现", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("A3", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{ }")]
    [InlineData("""{ "nodes": [] }""")]
    [InlineData("""{ "nodes": [ { "id": "a", "type": "Start", "ref": "r", "timeoutMs": 1000, "async": true } ] }""")]
    public void Valid_dsl_passes_schema_validation(string json)
    {
        DslSchemaValidator.ValidateJson(json);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_dsl_is_ignored_by_schema_validator(string? json)
    {
        // 空文本交给后续反序列化处理，这里不重复报错，避免同一问题抛两次。
        DslSchemaValidator.ValidateJson(json);
    }

    [Fact]
    public void Malformed_json_is_reported_as_such()
    {
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(
            () => DslSchemaValidator.ValidateJson("{ not json"));
        Assert.Contains("合法 JSON", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_object_root_does_not_throw()
    {
        // 数组/标量根不是合法 DSL，但结构校验只负责字段，交给反序列化报错。
        DslSchemaValidator.ValidateJson("[]");
    }

    [Fact]
    public void All_problems_are_reported_not_just_the_first()
    {
        // 逐个报错会把用户的发布流程拖成"改一个、再发布、再报下一个"。
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(() => DslSchemaValidator.ValidateJson(
            """
            {
              "nodes": [
                { "id": "a", "type": "Start", "bogusOne": 1, "bogusTwo": 2 }
              ]
            }
            """));

        Assert.Contains("bogusOne", ex.Message, StringComparison.Ordinal);
        Assert.Contains("bogusTwo", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Problem_list_is_capped_with_a_count_of_the_remainder()
    {
        // 字段很多时不能把整屏都塞进异常消息，但必须告知"还有多少"。
        var nodes = string.Join(",", System.Linq.Enumerable.Range(0, 12)
            .Select(i => $$"""{ "id": "n{{i}}", "type": "Start", "unknownField{{i}}" : 1 }"""));
        var ex = Assert.Throws<Volo.Abp.UserFriendlyException>(
            () => DslSchemaValidator.ValidateJson($$"""{ "nodes": [ {{nodes}} ] }"""));

        Assert.Contains("另有", ex.Message, StringComparison.Ordinal);
    }

    // ==================================================================
    // A3：异步作业状态机
    // ==================================================================

    private static FlowAsyncJob NewJob() =>
        new(Guid.NewGuid(), Guid.NewGuid(), "node-1", "flow-key", "POST", "https://example.com/hook", "{}");

    [Fact]
    public void New_job_starts_as_queued_without_completion_time()
    {
        var job = NewJob();

        Assert.Equal(FlowAsyncJobStatus.Queued, job.Status);
        Assert.Null(job.CompletedAt);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public void New_job_carries_instance_tenant_and_request_context()
    {
        var instanceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var job = new FlowAsyncJob(
            Guid.NewGuid(), instanceId, "n", "f", "POST", "https://example.com", "{\"a\":1}", tenantId);

        // 这四项是"作业能被回收/审计"的最低信息，缺一则排障无门。
        Assert.Equal(instanceId, job.InstanceId);
        Assert.Equal(tenantId, job.TenantId);
        Assert.Equal("n", job.NodeId);
        Assert.Equal("f", job.FlowKey);
    }

    [Fact]
    public void MarkRunning_increments_attempt_count()
    {
        var job = NewJob();

        job.MarkRunning();
        job.MarkRunning();

        Assert.Equal(FlowAsyncJobStatus.Running, job.Status);
        Assert.Equal(2, job.AttemptCount);
    }

    [Fact]
    public void MarkRunning_on_terminal_job_is_a_no_op()
    {
        // 终态不可被回退：否则 worker 重复消费会让"已失败"重新变"执行中"。
        var job = NewJob();
        job.MarkFailed("boom");

        job.MarkRunning();

        Assert.Equal(FlowAsyncJobStatus.Failed, job.Status);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public void MarkSucceeded_records_response_and_completion_time()
    {
        var job = NewJob();

        job.MarkSucceeded(201, "{\"ok\":true}");

        Assert.Equal(FlowAsyncJobStatus.Succeeded, job.Status);
        Assert.Equal(201, job.ResponseStatusCode);
        Assert.Equal("{\"ok\":true}", job.ResponseBody);
        Assert.NotNull(job.CompletedAt);
        Assert.Null(job.Error);
    }

    [Fact]
    public void MarkSucceeded_clears_a_previous_error()
    {
        // 重试成功后若仍留着上次错误，运维会看到"成功但有错误"的自相矛盾状态。
        var job = NewJob();
        job.MarkFailed("transient timeout");

        job.MarkSucceeded(200, "{}");

        Assert.Null(job.Error);
    }

    [Fact]
    public void MarkFailed_records_reason_and_completion_time()
    {
        var job = NewJob();

        job.MarkFailed("HttpRequestException: timeout");

        Assert.Equal(FlowAsyncJobStatus.Failed, job.Status);
        Assert.Equal("HttpRequestException: timeout", job.Error);
        Assert.NotNull(job.CompletedAt);
    }

    [Fact]
    public void Cancel_records_a_default_reason()
    {
        var job = NewJob();

        job.Cancel();

        Assert.Equal(FlowAsyncJobStatus.Canceled, job.Status);
        Assert.False(string.IsNullOrWhiteSpace(job.Error));
    }

    [Theory]
    [InlineData(FlowAsyncJobStatus.Succeeded)]
    [InlineData(FlowAsyncJobStatus.Failed)]
    public void Cancel_does_not_override_a_settled_outcome(FlowAsyncJobStatus settled)
    {
        // 结果已定就不该被取消覆盖，否则"已完成"会被误报成"被取消"。
        var job = NewJob();
        if (settled == FlowAsyncJobStatus.Succeeded)
        {
            job.MarkSucceeded(200, "{}");
        }
        else
        {
            job.MarkFailed("boom");
        }

        job.Cancel("late cancel");

        Assert.Equal(settled, job.Status);
    }

    [Fact]
    public void Cancel_is_allowed_while_still_running()
    {
        // 运行中取消是正常业务动作（流程被中止），必须允许。
        var job = NewJob();
        job.MarkRunning();

        job.Cancel("flow aborted");

        Assert.Equal(FlowAsyncJobStatus.Canceled, job.Status);
        Assert.Equal("flow aborted", job.Error);
    }

    [Fact]
    public void Status_enum_covers_the_whole_lifecycle()
    {
        // 枚举缺值会让持久化文档与状态机对不上，故显式钉住。
        Assert.Equal(
            new[] { "Queued", "Running", "Succeeded", "Failed", "Canceled" },
            Enum.GetNames<FlowAsyncJobStatus>()
        );
    }

    [Fact]
    public void Timeout_exception_carries_diagnosable_context()
    {
        // 超时若只抛字符串，上层无法区分"失败"与"等不到"，只能靠匹配文案——不可维护。
        var jobId = Guid.NewGuid();
        var ex = new FlowAsyncJobTimeoutException(jobId, TimeSpan.FromSeconds(30), FlowAsyncJobStatus.Running);

        Assert.Equal(jobId, ex.JobId);
        Assert.Equal(TimeSpan.FromSeconds(30), ex.Timeout);
        Assert.Equal(FlowAsyncJobStatus.Running, ex.LastStatus);
    }
}