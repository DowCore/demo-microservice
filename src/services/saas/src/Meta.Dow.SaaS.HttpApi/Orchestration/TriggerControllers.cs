using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/message-sources")]
public class MessageSourceController : SaaSController, IMessageSourceAppService
{
    private readonly IMessageSourceAppService _service;

    public MessageSourceController(IMessageSourceAppService service) => _service = service;

    [HttpGet]
    public Task<PagedResultDto<MessageSourceDto>> GetListAsync(MessageSourceGetListInput input) =>
        _service.GetListAsync(input);

    // 静态子路径必须在 {id} 之前，否则 "lookup" 会被当成 Guid 绑定失败
    [HttpGet]
    [Route("lookup")]
    public Task<ListResultDto<MessageSourceLookupDto>> GetLookupAsync() => _service.GetLookupAsync();

    [HttpGet]
    [Route("providers")]
    public Task<ListResultDto<MessageSourceProviderOptionDto>> GetProvidersAsync() =>
        _service.GetProvidersAsync();

    [HttpGet]
    [Route("{id}")]
    public Task<MessageSourceDto> GetAsync(Guid id) => _service.GetAsync(id);

    [HttpPost]
    public Task<MessageSourceDto> CreateAsync(CreateMessageSourceDto input) =>
        _service.CreateAsync(input);

    [HttpPut]
    [Route("{id}")]
    public Task<MessageSourceDto> UpdateAsync(Guid id, UpdateMessageSourceDto input) =>
        _service.UpdateAsync(id, input);

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
}

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/triggers")]
public class FlowTriggerController : SaaSController, IFlowTriggerAppService
{
    private readonly IFlowTriggerAppService _service;

    public FlowTriggerController(IFlowTriggerAppService service) => _service = service;

    [HttpGet]
    public Task<PagedResultDto<FlowTriggerDto>> GetListAsync(FlowTriggerGetListInput input) =>
        _service.GetListAsync(input);

    [HttpGet]
    [Route("{id}")]
    public Task<FlowTriggerDto> GetAsync(Guid id) => _service.GetAsync(id);

    [HttpPost]
    public Task<FlowTriggerDto> CreateAsync(CreateFlowTriggerDto input) =>
        _service.CreateAsync(input);

    [HttpPut]
    [Route("{id}")]
    public Task<FlowTriggerDto> UpdateAsync(Guid id, UpdateFlowTriggerDto input) =>
        _service.UpdateAsync(id, input);

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);
}

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/schedules")]
public class FlowScheduleController : SaaSController, IFlowScheduleAppService
{
    private readonly IFlowScheduleAppService _service;

    public FlowScheduleController(IFlowScheduleAppService service) => _service = service;

    [HttpGet]
    public Task<PagedResultDto<FlowScheduleDto>> GetListAsync(FlowScheduleGetListInput input) =>
        _service.GetListAsync(input);

    [HttpGet]
    [Route("{id}")]
    public Task<FlowScheduleDto> GetAsync(Guid id) => _service.GetAsync(id);

    [HttpPost]
    public Task<FlowScheduleDto> CreateAsync(CreateFlowScheduleDto input) =>
        _service.CreateAsync(input);

    [HttpPut]
    [Route("{id}")]
    public Task<FlowScheduleDto> UpdateAsync(Guid id, UpdateFlowScheduleDto input) =>
        _service.UpdateAsync(id, input);

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);

    [HttpPost]
    [Route("{id}/run-now")]
    public Task<LogicRunResultDto> RunNowAsync(Guid id) => _service.RunNowAsync(id);
}
