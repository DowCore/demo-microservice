using System;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Orchestration;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/instances")]
public class FlowInstanceController : SaaSController, IFlowInstanceAppService
{
    private readonly IFlowInstanceAppService _service;

    public FlowInstanceController(IFlowInstanceAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedResultDto<FlowInstanceDto>> GetListAsync(FlowInstanceGetListInput input)
    {
        return _service.GetListAsync(input);
    }

    [HttpGet]
    [Route("{id}")]
    public Task<FlowInstanceDto> GetAsync(Guid id)
    {
        return _service.GetAsync(id);
    }

    [HttpPost]
    public Task<FlowInstanceDto> StartAsync(StartFlowInstanceDto input)
    {
        return _service.StartAsync(input);
    }

    [HttpPost]
    [Route("dry-run")]
    public Task<FlowInstanceDto> DryRunAsync(DryRunFlowInstanceDto input)
    {
        return _service.DryRunAsync(input);
    }

    [HttpPost]
    [Route("{id}/cancel")]
    public Task CancelAsync(Guid id)
    {
        return _service.CancelAsync(id);
    }
}
