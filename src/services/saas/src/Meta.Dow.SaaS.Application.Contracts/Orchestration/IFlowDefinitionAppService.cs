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
}
