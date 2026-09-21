using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Meta.Dow.SaaS.Orchestration;

public interface IMessageSourceAppService : IApplicationService
{
    Task<PagedResultDto<MessageSourceDto>> GetListAsync(MessageSourceGetListInput input);
    Task<MessageSourceDto> GetAsync(Guid id);
    Task<ListResultDto<MessageSourceLookupDto>> GetLookupAsync();
    Task<ListResultDto<MessageSourceProviderOptionDto>> GetProvidersAsync();
    Task<MessageSourceDto> CreateAsync(CreateMessageSourceDto input);
    Task<MessageSourceDto> UpdateAsync(Guid id, UpdateMessageSourceDto input);
    Task DeleteAsync(Guid id);
}

public interface IFlowTriggerAppService : IApplicationService
{
    Task<PagedResultDto<FlowTriggerDto>> GetListAsync(FlowTriggerGetListInput input);
    Task<FlowTriggerDto> GetAsync(Guid id);
    Task<FlowTriggerDto> CreateAsync(CreateFlowTriggerDto input);
    Task<FlowTriggerDto> UpdateAsync(Guid id, UpdateFlowTriggerDto input);
    Task DeleteAsync(Guid id);
}

public interface IFlowScheduleAppService : IApplicationService
{
    Task<PagedResultDto<FlowScheduleDto>> GetListAsync(FlowScheduleGetListInput input);
    Task<FlowScheduleDto> GetAsync(Guid id);
    Task<FlowScheduleDto> CreateAsync(CreateFlowScheduleDto input);
    Task<FlowScheduleDto> UpdateAsync(Guid id, UpdateFlowScheduleDto input);
    Task DeleteAsync(Guid id);
    /// <summary>立即触发一次（调试）</summary>
    Task<LogicRunResultDto> RunNowAsync(Guid id);
}
