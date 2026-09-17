using System;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Orchestration;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/definitions")]
public class FlowDefinitionController : SaaSController, IFlowDefinitionAppService
{
    private readonly IFlowDefinitionAppService _service;

    public FlowDefinitionController(IFlowDefinitionAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedResultDto<FlowDefinitionDto>> GetListAsync(FlowDefinitionGetListInput input)
    {
        return _service.GetListAsync(input);
    }

    [HttpGet]
    [Route("{id}")]
    public Task<FlowDefinitionDto> GetAsync(Guid id)
    {
        return _service.GetAsync(id);
    }

    [HttpPost]
    public Task<FlowDefinitionDto> CreateAsync(CreateFlowDefinitionDto input)
    {
        return _service.CreateAsync(input);
    }

    [HttpPut]
    [Route("{id}")]
    public Task<FlowDefinitionDto> UpdateAsync(Guid id, UpdateFlowDefinitionDto input)
    {
        return _service.UpdateAsync(id, input);
    }

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id)
    {
        return _service.DeleteAsync(id);
    }

    [HttpPost]
    [Route("{id}/publish")]
    public Task<FlowVersionDto> PublishAsync(Guid id)
    {
        return _service.PublishAsync(id);
    }

    [HttpGet]
    [Route("{id}/versions")]
    public Task<ListResultDto<FlowVersionDto>> GetVersionsAsync(Guid id)
    {
        return _service.GetVersionsAsync(id);
    }
}
