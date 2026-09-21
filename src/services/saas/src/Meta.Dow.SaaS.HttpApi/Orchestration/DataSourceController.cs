using System;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Orchestration;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/data-sources")]
public class DataSourceController : SaaSController, IDataSourceAppService
{
    private readonly IDataSourceAppService _service;

    public DataSourceController(IDataSourceAppService service)
    {
        _service = service;
    }

    [HttpGet]
    public Task<PagedResultDto<DataSourceDto>> GetListAsync(DataSourceGetListInput input)
    {
        return _service.GetListAsync(input);
    }

    [HttpGet]
    [Route("lookup")]
    public Task<ListResultDto<DataSourceLookupDto>> GetLookupAsync()
    {
        return _service.GetLookupAsync();
    }

    [HttpGet]
    [Route("providers")]
    public Task<ListResultDto<DataSourceProviderOptionDto>> GetProvidersAsync()
    {
        return _service.GetProvidersAsync();
    }

    [HttpPost]
    [Route("test")]
    public Task<DataSourceTestResultDto> TestAsync(TestDataSourceConnectionDto input)
    {
        return _service.TestAsync(input);
    }

    [HttpPost]
    [Route("{id}/test")]
    public Task<DataSourceTestResultDto> TestByIdAsync(Guid id)
    {
        return _service.TestAsync(new TestDataSourceConnectionDto { Id = id });
    }

    [HttpGet]
    [Route("{id}")]
    public Task<DataSourceDto> GetAsync(Guid id)
    {
        return _service.GetAsync(id);
    }

    [HttpPost]
    public Task<DataSourceDto> CreateAsync(CreateDataSourceDto input)
    {
        return _service.CreateAsync(input);
    }

    [HttpPut]
    [Route("{id}")]
    public Task<DataSourceDto> UpdateAsync(Guid id, UpdateDataSourceDto input)
    {
        return _service.UpdateAsync(id, input);
    }

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id)
    {
        return _service.DeleteAsync(id);
    }
}
