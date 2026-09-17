using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

[Authorize]
public class LogicApiAppService : SaaSAppService, ILogicApiAppService
{
    private readonly IRepository<FlowInstance, Guid> _instanceRepository;
    private readonly FlowExecutor _flowExecutor;
    private readonly PublishedFlowCache _publishedFlowCache;
    private readonly SystemContextBuilder _systemContextBuilder;

    public LogicApiAppService(
        IRepository<FlowInstance, Guid> instanceRepository,
        FlowExecutor flowExecutor,
        PublishedFlowCache publishedFlowCache,
        SystemContextBuilder systemContextBuilder
    )
    {
        _instanceRepository = instanceRepository;
        _flowExecutor = flowExecutor;
        _publishedFlowCache = publishedFlowCache;
        _systemContextBuilder = systemContextBuilder;
    }

    /// <summary>
    /// 已发布逻辑的系统 API。调用权使用 Instances.Run（可后续拆 Logic.{flowKey}）。
    /// </summary>
    [Authorize(OrchestrationPermissions.Instances.Run)]
    public async Task<LogicRunResultDto> RunAsync(string flowKey, string? bodyJson)
    {
        var totalSw = Stopwatch.StartNew();

        var lookupSw = Stopwatch.StartNew();
        var published = await _publishedFlowCache.GetByKeyAsync(flowKey);
        lookupSw.Stop();

        var instance = new FlowInstance(
            GuidGenerator.Create(),
            published.DefinitionId,
            published.DefinitionName,
            published.Version,
            isDryRun: false,
            bodyJson,
            CurrentTenant.Id
        );

        var executeSw = Stopwatch.StartNew();
        var execution = await _flowExecutor.ExecuteAsync(
            published.DslJson,
            bodyJson,
            isDryRun: false,
            filterOutputsByRole: true,
            currentUser: CurrentUser,
            skipValidation: true
        );
        executeSw.Stop();

        foreach (var node in execution.Nodes)
        {
            instance.AddNode(node);
        }

        if (execution.Succeeded)
        {
            instance.Succeed(execution.VariablesJson);
        }
        else
        {
            instance.Fail(execution.Error ?? "Failed", execution.VariablesJson);
        }

        var persistSw = Stopwatch.StartNew();
        await _instanceRepository.InsertAsync(instance, autoSave: true);
        persistSw.Stop();

        var omitted = Math.Max(0, execution.ContractFieldCount - execution.VisibleFields.Count);
        totalSw.Stop();

        return new LogicRunResultDto
        {
            Success = execution.Succeeded,
            InstanceId = instance.Id,
            DataJson = execution.OutputDataJson,
            Error = execution.Error,
            Meta = new LogicRunMetaDto
            {
                VisibleFields = execution.VisibleFields,
                OmittedFieldCount = omitted,
                FlowKey = published.FlowKey,
                Version = published.Version,
                LookupMs = (int)lookupSw.ElapsedMilliseconds,
                ExecuteMs = execution.ExecuteMs > 0 ? execution.ExecuteMs : (int)executeSw.ElapsedMilliseconds,
                PersistMs = (int)persistSw.ElapsedMilliseconds,
                TotalMs = (int)totalSw.ElapsedMilliseconds
            }
        };
    }

    public Task<SystemParameterCatalogDto> GetSystemParametersAsync()
    {
        return Task.FromResult(
            new SystemParameterCatalogDto
            {
                FixedKeys =
                [
                    new() { Key = "sys.userId", Category = "user", Description = "当前用户 Id" },
                    new() { Key = "sys.userName", Category = "user", Description = "当前用户名" },
                    new() { Key = "sys.userEmail", Category = "user", Description = "当前用户邮箱" },
                    new() { Key = "sys.tenantId", Category = "tenant", Description = "当前租户 Id" },
                    new() { Key = "sys.tenantName", Category = "tenant", Description = "当前租户名" },
                    new() { Key = "sys.culture", Category = "request", Description = "当前 UI 文化" },
                    new() { Key = "sys.timeZone", Category = "request", Description = "时区 Id" },
                    new() { Key = "sys.correlationId", Category = "request", Description = "关联 Id" },
                    new() { Key = "sys.Now", Category = "date", Description = "当前时间" },
                    new() { Key = "sys.Today", Category = "date", Description = "今天 00:00" },
                    new() { Key = "sys.MonthStart", Category = "date", Description = "本月起始" },
                    new() { Key = "sys.MonthEnd", Category = "date", Description = "本月结束" }
                ],
                DateExpressionExamples =
                [
                    "sys.Today",
                    "sys.Now + 3d",
                    "sys.MonthStart + 1M | monthEnd",
                    "sys.Today - 1w | weekStart"
                ]
            }
        );
    }

    public Task<DateExpressionPreviewDto> PreviewDateExpressionAsync(DateExpressionPreviewInput input)
    {
        var snapshot = _systemContextBuilder.Build(input.TimeZone);
        var value = DateExpressionEvaluator.Evaluate(input.Expression, snapshot.Now, snapshot.TimeZone);
        return Task.FromResult(
            new DateExpressionPreviewDto
            {
                Expression = input.Expression,
                Value = value.ToString("o")
            }
        );
    }
}
