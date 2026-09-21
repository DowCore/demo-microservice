using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Meta.Dow.SaaS.Orchestration;

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/tables")]
public class TableDefinitionController : SaaSController, ITableDefinitionAppService
{
    private readonly ITableDefinitionAppService _service;

    public TableDefinitionController(ITableDefinitionAppService service) => _service = service;

    [HttpGet]
    public Task<PagedResultDto<TableDefinitionDto>> GetListAsync(TableDefinitionGetListInput input) =>
        _service.GetListAsync(input);

    [HttpGet]
    [Route("{id}")]
    public Task<TableDefinitionDto> GetAsync(Guid id) => _service.GetAsync(id);

    [HttpPost]
    public Task<TableDefinitionDto> CreateAsync(CreateTableDefinitionDto input) =>
        _service.CreateAsync(input);

    [HttpPut]
    [Route("{id}")]
    public Task<TableDefinitionDto> UpdateAsync(Guid id, UpdateTableDefinitionDto input) =>
        _service.UpdateAsync(id, input);

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);

    [HttpGet]
    [Route("{id}/preview")]
    public Task<ListResultDto<DdlPreviewItemDto>> PreviewAsync(Guid id) =>
        _service.PreviewAsync(id);

    [HttpPost]
    [Route("{id}/apply")]
    public Task<TableDefinitionDto> ApplyAsync(Guid id) => _service.ApplyAsync(id);

    [HttpPost]
    [Route("{id}/resources")]
    public Task<AppResourceDto> CreateResourceAsync(Guid id, CreateAppResourceDto input) =>
        _service.CreateResourceAsync(id, input);
}

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/resources")]
public class AppResourceController : SaaSController, IAppResourceAppService
{
    private readonly IAppResourceAppService _service;

    public AppResourceController(IAppResourceAppService service) => _service = service;

    [HttpGet]
    public Task<PagedResultDto<AppResourceDto>> GetListAsync(AppResourceGetListInput input) =>
        _service.GetListAsync(input);

    [HttpGet]
    [Route("by-code/{code}")]
    public Task<AppResourceDto> GetByCodeAsync(string code) => _service.GetByCodeAsync(code);

    [HttpGet]
    [Route("published/{code}")]
    public Task<AppResourceDto> GetPublishedByCodeAsync(string code) =>
        _service.GetPublishedByCodeAsync(code);

    [HttpGet]
    [Route("{id}")]
    public Task<AppResourceDto> GetAsync(Guid id) => _service.GetAsync(id);

    [HttpPut]
    [Route("{id}")]
    public Task<AppResourceDto> UpdateAsync(Guid id, UpdateAppResourceDto input) =>
        _service.UpdateAsync(id, input);

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);

    [HttpPost]
    [Route("{id}/publish")]
    public Task<AppResourceDto> PublishAsync(Guid id) => _service.PublishAsync(id);

    [HttpPost]
    [Route("{code}/query")]
    public Task<ResourceInvokeResultDto> QueryAsync(string code, ResourceRuntimeQueryInput input) =>
        _service.QueryAsync(code, input);

    [HttpPost]
    [Route("{code}/get")]
    public Task<ResourceInvokeResultDto> GetRecordAsync(string code, ResourceRuntimeGetInput input) =>
        _service.GetRecordAsync(code, input);

    [HttpPost]
    [Route("{code}/create")]
    public Task<ResourceInvokeResultDto> CreateRecordAsync(string code, ResourceRuntimeSaveInput input) =>
        _service.CreateRecordAsync(code, input);

    [HttpPost]
    [Route("{code}/update")]
    public Task<ResourceInvokeResultDto> UpdateRecordAsync(string code, ResourceRuntimeSaveInput input) =>
        _service.UpdateRecordAsync(code, input);

    [HttpPost]
    [Route("{code}/delete")]
    public Task<ResourceInvokeResultDto> DeleteRecordAsync(string code, ResourceRuntimeGetInput input) =>
        _service.DeleteRecordAsync(code, input);
}

[Area(SaaSRemoteServiceConsts.ModuleName)]
[RemoteService(Name = SaaSRemoteServiceConsts.RemoteServiceName)]
[Route("api/orchestration/reports")]
public class ReportDefinitionController : SaaSController, IReportDefinitionAppService
{
    private readonly IReportDefinitionAppService _service;

    public ReportDefinitionController(IReportDefinitionAppService service) => _service = service;

    [HttpGet]
    public Task<PagedResultDto<ReportDefinitionDto>> GetListAsync(ReportDefinitionGetListInput input) =>
        _service.GetListAsync(input);

    [HttpGet]
    [Route("{id}")]
    public Task<ReportDefinitionDto> GetAsync(Guid id) => _service.GetAsync(id);

    [HttpGet]
    [Route("by-code/{code}")]
    public Task<ReportDefinitionDto> GetByCodeAsync(string code) => _service.GetByCodeAsync(code);

    [HttpGet]
    [Route("published/{code}")]
    public Task<ReportDefinitionDto> GetPublishedByCodeAsync(string code) =>
        _service.GetPublishedByCodeAsync(code);

    [HttpPost]
    public Task<ReportDefinitionDto> CreateAsync(CreateReportDefinitionDto input) =>
        _service.CreateAsync(input);

    [HttpPost]
    [Route("from-resource")]
    public Task<ReportDefinitionDto> CreateFromResourceAsync(CreateReportFromResourceDto input) =>
        _service.CreateFromResourceAsync(input);

    [HttpPut]
    [Route("{id}")]
    public Task<ReportDefinitionDto> UpdateAsync(Guid id, UpdateReportDefinitionDto input) =>
        _service.UpdateAsync(id, input);

    [HttpDelete]
    [Route("{id}")]
    public Task DeleteAsync(Guid id) => _service.DeleteAsync(id);

    [HttpPost]
    [Route("{id}/publish")]
    public Task<ReportDefinitionDto> PublishAsync(Guid id) => _service.PublishAsync(id);

    [HttpPost]
    [Route("{code}/query")]
    public Task<ResourceInvokeResultDto> QueryAsync(string code, ResourceRuntimeQueryInput input) =>
        _service.QueryAsync(code, input);
}
