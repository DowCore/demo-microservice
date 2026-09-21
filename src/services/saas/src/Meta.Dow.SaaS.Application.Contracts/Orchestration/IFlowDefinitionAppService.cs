using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Meta.Dow.SaaS.Orchestration;

public interface IFlowDefinitionAppService : IApplicationService
{
    Task<PagedResultDto<FlowDefinitionDto>> GetListAsync(FlowDefinitionGetListInput input);

    Task<FlowDefinitionDto> GetAsync(Guid id);

    Task<FlowDefinitionDto> CreateAsync(CreateFlowDefinitionDto input);

    Task<FlowDefinitionDto> UpdateAsync(Guid id, UpdateFlowDefinitionDto input);

    Task DeleteAsync(Guid id);

    Task<FlowVersionDto> PublishAsync(Guid id);

    Task<ListResultDto<FlowVersionDto>> GetVersionsAsync(Guid id);

    /// <summary>已发布且标记可复用的逻辑组件（设计器 SubFlow 选用）</summary>
    Task<ListResultDto<ReusableFlowLookupDto>> GetReusableLookupAsync(string? filter = null);

    /// <summary>谁引用了该 flowKey（被调用方视角）</summary>
    Task<ListResultDto<FlowUsageDto>> GetUsagesByCalleeAsync(string flowKey);

    /// <summary>该定义 DSL 里调用了哪些 SubFlow（调用方视角）</summary>
    Task<ListResultDto<FlowUsageDto>> GetUsagesByCallerAsync(Guid definitionId);
}
