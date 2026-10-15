using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

[Authorize(OrchestrationPermissions.Definitions.Default)]
public class FlowDefinitionAppService : SaaSAppService, IFlowDefinitionAppService
{
    private readonly IRepository<FlowDefinition, Guid> _definitionRepository;
    private readonly IRepository<FlowVersion, Guid> _versionRepository;
    private readonly IRepository<FlowUsage, Guid> _usageRepository;
    private readonly PublishedFlowCache _publishedFlowCache;
    private readonly FlowDefinitionInvalidationBroadcaster _invalidationBroadcaster;
    private readonly IFlowUsageIndexer _usageIndexer;

    public FlowDefinitionAppService(
        IRepository<FlowDefinition, Guid> definitionRepository,
        IRepository<FlowVersion, Guid> versionRepository,
        IRepository<FlowUsage, Guid> usageRepository,
        PublishedFlowCache publishedFlowCache,
        FlowDefinitionInvalidationBroadcaster invalidationBroadcaster,
        IFlowUsageIndexer usageIndexer
    )
    {
        _definitionRepository = definitionRepository;
        _versionRepository = versionRepository;
        _usageRepository = usageRepository;
        _publishedFlowCache = publishedFlowCache;
        _invalidationBroadcaster = invalidationBroadcaster;
        _usageIndexer = usageIndexer;
    }

    public async Task<PagedResultDto<FlowDefinitionDto>> GetListAsync(FlowDefinitionGetListInput input)
    {
        var query = await _definitionRepository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            query = query.Where(x => x.Name.Contains(filter) || x.Code.Contains(filter));
        }

        if (input.Status.HasValue)
        {
            query = query.Where(x => x.Status == input.Status.Value);
        }

        if (input.IsReusable.HasValue)
        {
            query = query.Where(x => x.IsReusable == input.IsReusable.Value);
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();

        return new PagedResultDto<FlowDefinitionDto>(
            total,
            ObjectMapper.Map<List<FlowDefinition>, List<FlowDefinitionDto>>(items)
        );
    }

    public async Task<FlowDefinitionDto> GetAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Create)]
    public async Task<FlowDefinitionDto> CreateAsync(CreateFlowDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var code = input.Code.Trim();
        if (SystemResourceFlowKeys.IsSystem(code) || code.StartsWith("sys.", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        if (await _definitionRepository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:DefinitionCodeAlreadyExists", code]);
        }

        var entity = new FlowDefinition(
            GuidGenerator.Create(),
            input.Name.Trim(),
            code,
            input.Category?.Trim(),
            CurrentTenant.Id,
            input.IsReusable
        );
        entity.UpdateDraft(
            input.Name.Trim(),
            input.Category?.Trim(),
            input.GraphJson,
            input.DslJson,
            input.IsReusable
        );

        await _definitionRepository.InsertAsync(entity, autoSave: true);
        await _usageIndexer.RebuildForDefinitionAsync(entity);
        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    public async Task<FlowDefinitionDto> GetByCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new UserFriendlyException(L["Orchestration:FlowKeyRequired"]);
        }

        var key = code.Trim();
        var entity = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == key);
        if (entity == null)
        {
            throw new UserFriendlyException(L["Orchestration:DefinitionNotFound", key]);
        }

        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    /// <summary>
    /// 表单×流程升级：复制系统 CRUD 流 DSL 为可编辑草稿（系统源本身仍只读）。
    /// </summary>
    [Authorize(OrchestrationPermissions.Definitions.Create)]
    public async Task<FlowDefinitionDto> CloneAsCustomAsync(CloneFlowDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var sourceCode = input.SourceCode.Trim();
        var newCode = input.NewCode.Trim();
        if (string.IsNullOrWhiteSpace(sourceCode) || string.IsNullOrWhiteSpace(newCode))
        {
            throw new UserFriendlyException(L["Orchestration:FlowKeyRequired"]);
        }

        if (SystemResourceFlowKeys.IsSystem(newCode) || newCode.StartsWith("sys.", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        if (await _definitionRepository.AnyAsync(x => x.Code == newCode))
        {
            throw new UserFriendlyException(L["Orchestration:DefinitionCodeAlreadyExists", newCode]);
        }

        var source = await _definitionRepository.FirstOrDefaultAsync(x => x.Code == sourceCode);
        if (source == null)
        {
            throw new UserFriendlyException(L["Orchestration:DefinitionNotFound", sourceCode]);
        }

        var dslJson = source.DslJson;
        var graphJson = source.GraphJson ?? "{}";
        if (string.IsNullOrWhiteSpace(dslJson) && source.PublishedVersion is > 0)
        {
            var versions = await _versionRepository.GetQueryableAsync();
            var published = versions
                .Where(x => x.DefinitionId == source.Id && x.Version == source.PublishedVersion)
                .OrderByDescending(x => x.CreationTime)
                .FirstOrDefault();
            if (published != null)
            {
                dslJson = published.DslJson;
                graphJson = published.GraphJson;
            }
        }

        if (string.IsNullOrWhiteSpace(dslJson))
        {
            throw new UserFriendlyException(L["Orchestration:DslRequired"]);
        }

        var created = await CreateAsync(
            new CreateFlowDefinitionDto
            {
                Name = input.NewName.Trim(),
                Code = newCode,
                Category = string.IsNullOrWhiteSpace(input.Category)
                    ? (source.Category ?? "自定义")
                    : input.Category.Trim(),
                GraphJson = graphJson,
                DslJson = dslJson,
                IsReusable = input.IsReusable
            }
        );

        if (input.Publish)
        {
            await PublishAsync(created.Id);
            return await GetAsync(created.Id);
        }

        return created;
    }

    [Authorize(OrchestrationPermissions.Definitions.Update)]
    public async Task<FlowDefinitionDto> UpdateAsync(Guid id, UpdateFlowDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entity = await _definitionRepository.GetAsync(id);
        if (IsImmutableSystem(entity))
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        entity.UpdateDraft(
            input.Name.Trim(),
            input.Category?.Trim(),
            input.GraphJson,
            input.DslJson,
            input.IsReusable
        );
        await _definitionRepository.UpdateAsync(entity, autoSave: true);
        await _usageIndexer.RebuildForDefinitionAsync(entity);
        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        if (entity.IsSystem)
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        await _usageIndexer.ClearForDefinitionAsync(id);

        // A5：删除也必须失效缓存。原实现只有 PublishAsync 失效，
        // 导致已删除的流程在本进程内最长 10 分钟仍可被调用。
        _publishedFlowCache.InvalidateByDefinition(entity);
        await _invalidationBroadcaster.BroadcastAsync();

        await _definitionRepository.DeleteAsync(id, autoSave: true);
    }

    [Authorize(OrchestrationPermissions.Definitions.Publish)]
    public async Task<FlowDefinitionDto> DisableAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        if (entity.IsSystem)
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        if (entity.Status != FlowDefinitionStatus.Published)
        {
            throw new UserFriendlyException("只有已发布的流程可以停用。");
        }

        entity.Disable();
        await _definitionRepository.UpdateAsync(entity, autoSave: true);

        // 停用必须立即失效本地快照并广播到其他副本，
        // 否则被停用的流程在最长 10 分钟内仍可被调用。
        _publishedFlowCache.InvalidateByDefinition(entity);
        await _invalidationBroadcaster.BroadcastAsync();

        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Publish)]
    public async Task<FlowDefinitionDto> EnableAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        if (entity.IsSystem)
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        if (entity.Status != FlowDefinitionStatus.Disabled)
        {
            throw new UserFriendlyException("只有已停用的流程可以重新启用。");
        }

        entity.EnableAsDraft();
        await _definitionRepository.UpdateAsync(entity, autoSave: true);

        _publishedFlowCache.InvalidateByDefinition(entity);
        await _invalidationBroadcaster.BroadcastAsync();

        return ObjectMapper.Map<FlowDefinition, FlowDefinitionDto>(entity);
    }

    [Authorize(OrchestrationPermissions.Definitions.Publish)]
    public async Task<FlowVersionDto> PublishAsync(Guid id)
    {
        var entity = await _definitionRepository.GetAsync(id);
        if (IsImmutableSystem(entity))
        {
            throw new UserFriendlyException(L["Orchestration:SystemFlowLocked"]);
        }

        if (string.IsNullOrWhiteSpace(entity.DslJson))
        {
            throw new UserFriendlyException(L["Orchestration:DslRequired"]);
        }

        try
        {
            // A1：未知 / 已知但未实现的字段在发布期就报错，不再静默忽略。
            // 顺序上先做结构校验——它给出的错误信息比后续反序列化更具体。
            DslSchemaValidator.ValidateJson(entity.DslJson);

            var dsl = System.Text.Json.JsonSerializer.Deserialize<FlowDslDocument>(
                entity.DslJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }
            );
            if (dsl == null)
            {
                throw new UserFriendlyException(L["Orchestration:DslRequired"]);
            }

            DslSchemaValidator.ValidateSemantics(dsl);
            FlowExecutor.ValidateDsl(dsl);
            ValidateOutputContracts(dsl);
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new UserFriendlyException(L["Orchestration:InvalidDsl", ex.Message]);
        }

        var nextVersion = (entity.PublishedVersion ?? 0) + 1;
        var version = new FlowVersion(
            GuidGenerator.Create(),
            entity.Id,
            nextVersion,
            entity.GraphJson ?? "{}",
            entity.DslJson!,
            CurrentTenant.Id
        );

        await _versionRepository.InsertAsync(version, autoSave: true);
        entity.MarkPublished(nextVersion);
        await _definitionRepository.UpdateAsync(entity, autoSave: true);
        await _usageIndexer.RebuildForDefinitionAsync(entity);
        _publishedFlowCache.InvalidateByDefinition(entity);
        await _invalidationBroadcaster.BroadcastAsync();

        return ObjectMapper.Map<FlowVersion, FlowVersionDto>(version);
    }

    /// <summary>
    /// 出参契约的结构校验。放在发布期，是因为这些冲突在运行期只会表现为
    /// "某个字段的值和预期不一样"，排查成本极高。
    ///
    /// <para><b>遍历范围必须覆盖全部"契约载体"</b>：根级 <c>dsl.Outputs</c>、
    /// 每个 End 节点的 <c>finalOutputs</c>，以及两者内部的<b>嵌套</b>字段
    /// （<c>map.item[].visibleTo</c>、<c>group.header[].visibleTo</c>、
    /// <c>group.children.visibleTo</c>）。只查根级等于给嵌套留后门——
    /// 而嵌套 visibleTo 拼错时 <c>OutputVisibilityFilter.IsVisible</c> 会走
    /// <c>return true</c> 兜底，导致"配了可见性却全可见"这类数据暴露。
    /// 本方法与 <c>FlowExecutor.ValidateVisibleTo</c> 是同一套规则的两个入口。</para>
    /// </summary>
    private static void ValidateOutputContracts(FlowDslDocument dsl)
    {
        foreach (var output in dsl.Outputs ?? [])
        {
            ValidateOutputContract(output, $"outputs.{output.Name}");
        }

        foreach (var end in (dsl.Nodes ?? [])
                     .Where(x => string.Equals(x.Type, "End", StringComparison.OrdinalIgnoreCase)))
        {
            foreach (var output in end.FinalOutputs ?? [])
            {
                ValidateOutputContract(output, $"End('{end.Id}').finalOutputs.{output.Name}");
            }
        }
    }

    /// <summary>
    /// 仅供测试直接触达出参契约校验。<b>production 代码不应调用</b>——
    /// 校验必须经由 <see cref="PublishAsync"/>，那里才有"发布失败要中止"的语义。
    /// </summary>
    internal static void ValidateOutputContractsForTest(FlowDslDocument dsl) => ValidateOutputContracts(dsl);

    /// <summary>校验单个出参及其全部嵌套字段。owner 用于错误信息定位。</summary>
    private static void ValidateOutputContract(FlowOutputParameterDsl output, string owner)
    {
        // C15：aggregate 与 map 同配时，ApplyAggregate 分支先 return，map 静默失效。
        // 两个都是"整形成一个值"的指令，同时声明就是配置冲突，必须让用户二选一。
        if (output.Aggregate != null && !string.IsNullOrWhiteSpace(output.Aggregate.Op) &&
            output.Map?.Item is { Count: > 0 })
        {
            throw new UserFriendlyException(
                $"出参 '{owner}' 同时配置了 aggregate 与 map.item，二者语义冲突。" +
                "aggregate 是把整个集合聚合成一个值，map.item 是逐项映射——请只保留其一。"
            );
        }

        // wrap 目前只识别 array；配了别的值等于没配。
        if (!string.IsNullOrWhiteSpace(output.Wrap) &&
            !output.Wrap.Trim().Equals("array", StringComparison.OrdinalIgnoreCase))
        {
            throw new UserFriendlyException(
                $"出参 '{owner}' 的 wrap='{output.Wrap}' 尚未实现；当前只支持 wrap='array'。"
            );
        }

        FlowExecutor.ValidateVisibleTo(owner, output.VisibleTo);

        // map.item 可无限层级嵌套，每一层都带 visibleTo。
        ValidateNestedVisibleTo(output.Map, $"{owner}.map");
        ValidateGroup(output.Group, $"{owner}.group");
    }

    /// <summary>递归校验 map 投影链上的 visibleTo（FlowOutputMapDsl → FlowOutputMapDsl 可自嵌套）。</summary>
    private static void ValidateNestedVisibleTo(FlowOutputMapDsl? map, string owner)
    {
        if (map?.Item is not { Count: > 0 })
        {
            return;
        }

        for (var i = 0; i < map.Item.Count; i++)
        {
            var item = map.Item[i];
            FlowExecutor.ValidateVisibleTo($"{owner}.item[{i}]", item.VisibleTo);
            ValidateNestedVisibleTo(item.Map, $"{owner}.item[{i}].map");
        }
    }

    private static void ValidateGroup(FlowOutputGroupDsl? group, string owner)
    {
        if (group == null)
        {
            return;
        }

        if (group.Header is { Count: > 0 })
        {
            for (var i = 0; i < group.Header.Count; i++)
            {
                FlowExecutor.ValidateVisibleTo($"{owner}.header[{i}]", group.Header[i].VisibleTo);
            }
        }

        if (group.Children != null)
        {
            FlowExecutor.ValidateVisibleTo($"{owner}.children", group.Children.VisibleTo);
            ValidateNestedVisibleTo(group.Children.Map, $"{owner}.children.map");
        }
    }

    public async Task<ListResultDto<FlowVersionDto>> GetVersionsAsync(Guid id)
    {
        await _definitionRepository.GetAsync(id);
        var query = await _versionRepository.GetQueryableAsync();
        var list = query
            .Where(x => x.DefinitionId == id)
            .OrderByDescending(x => x.Version)
            .ToList();

        return new ListResultDto<FlowVersionDto>(
            ObjectMapper.Map<List<FlowVersion>, List<FlowVersionDto>>(list)
        );
    }

    public async Task<ListResultDto<ReusableFlowLookupDto>> GetReusableLookupAsync(string? filter = null)
    {
        var query = await _definitionRepository.GetQueryableAsync();
        query = query.Where(x =>
            x.IsReusable &&
            x.Status == FlowDefinitionStatus.Published &&
            x.PublishedVersion != null
        );

        if (!string.IsNullOrWhiteSpace(filter))
        {
            var f = filter.Trim();
            query = query.Where(x => x.Name.Contains(f) || x.Code.Contains(f));
        }

        var list = query
            .OrderBy(x => x.Name)
            .Take(200)
            .ToList()
            .Select(x => new ReusableFlowLookupDto
            {
                Id = x.Id,
                Code = x.Code,
                Name = x.Name,
                Category = x.Category,
                PublishedVersion = x.PublishedVersion ?? 0
            })
            .ToList();

        return new ListResultDto<ReusableFlowLookupDto>(list);
    }

    public async Task<ListResultDto<FlowUsageDto>> GetUsagesByCalleeAsync(string flowKey)
    {
        if (string.IsNullOrWhiteSpace(flowKey))
        {
            throw new UserFriendlyException(L["Orchestration:FlowKeyRequired"]);
        }

        var key = flowKey.Trim();
        var query = await _usageRepository.GetQueryableAsync();
        var list = query
            .Where(x => x.CalleeFlowKey == key)
            .OrderByDescending(x => x.CreationTime)
            .Take(500)
            .ToList();

        return new ListResultDto<FlowUsageDto>(
            ObjectMapper.Map<List<FlowUsage>, List<FlowUsageDto>>(list)
        );
    }

    public async Task<ListResultDto<FlowUsageDto>> GetUsagesByCallerAsync(Guid definitionId)
    {
        await _definitionRepository.GetAsync(definitionId);
        var query = await _usageRepository.GetQueryableAsync();
        var list = query
            .Where(x => x.CallerDefinitionId == definitionId)
            .OrderBy(x => x.CalleeFlowKey)
            .Take(500)
            .ToList();

        return new ListResultDto<FlowUsageDto>(
            ObjectMapper.Map<List<FlowUsage>, List<FlowUsageDto>>(list)
        );
    }

    /// <summary>
    /// 资源系统流锁定。控制流演示（cf-）允许修改和发布，方便按示例改入参、出参和被调流程。
    /// 删除、停用仍锁定，避免演示被清掉。
    /// </summary>
    private static bool IsImmutableSystem(FlowDefinition entity)
    {
        if (!entity.IsSystem)
        {
            return false;
        }

        var code = entity.Code ?? "";
        return !code.StartsWith("cf-", StringComparison.OrdinalIgnoreCase);
    }
}
