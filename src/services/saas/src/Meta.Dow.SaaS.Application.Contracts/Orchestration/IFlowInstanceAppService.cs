using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Meta.Dow.SaaS.Orchestration;

public interface IFlowInstanceAppService : IApplicationService
{
    Task<PagedResultDto<FlowInstanceDto>> GetListAsync(FlowInstanceGetListInput input);

    Task<FlowInstanceDto> GetAsync(Guid id);

    Task<FlowInstanceDto> StartAsync(StartFlowInstanceDto input);

    Task<FlowInstanceDto> DryRunAsync(DryRunFlowInstanceDto input);

    Task CancelAsync(Guid id);
}
