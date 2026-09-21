using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Meta.Dow.SaaS.Orchestration;

public interface ITableDefinitionAppService : IApplicationService
{
    Task<PagedResultDto<TableDefinitionDto>> GetListAsync(TableDefinitionGetListInput input);

    Task<TableDefinitionDto> GetAsync(Guid id);

    Task<TableDefinitionDto> CreateAsync(CreateTableDefinitionDto input);

    Task<TableDefinitionDto> UpdateAsync(Guid id, UpdateTableDefinitionDto input);

    Task DeleteAsync(Guid id);

    Task<ListResultDto<DdlPreviewItemDto>> PreviewAsync(Guid id);

    Task<TableDefinitionDto> ApplyAsync(Guid id);

    Task<AppResourceDto> CreateResourceAsync(Guid id, CreateAppResourceDto input);
}

public interface IAppResourceAppService : IApplicationService
{
    Task<PagedResultDto<AppResourceDto>> GetListAsync(AppResourceGetListInput input);

    Task<AppResourceDto> GetAsync(Guid id);

    Task<AppResourceDto> GetByCodeAsync(string code);

    Task<AppResourceDto> UpdateAsync(Guid id, UpdateAppResourceDto input);

    Task DeleteAsync(Guid id);

    Task<AppResourceDto> PublishAsync(Guid id);

    Task<AppResourceDto> GetPublishedByCodeAsync(string code);

    Task<ResourceInvokeResultDto> QueryAsync(string code, ResourceRuntimeQueryInput input);

    Task<ResourceInvokeResultDto> GetRecordAsync(string code, ResourceRuntimeGetInput input);

    Task<ResourceInvokeResultDto> CreateRecordAsync(string code, ResourceRuntimeSaveInput input);

    Task<ResourceInvokeResultDto> UpdateRecordAsync(string code, ResourceRuntimeSaveInput input);

    Task<ResourceInvokeResultDto> DeleteRecordAsync(string code, ResourceRuntimeGetInput input);
}

public interface IReportDefinitionAppService : IApplicationService
{
    Task<PagedResultDto<ReportDefinitionDto>> GetListAsync(ReportDefinitionGetListInput input);

    Task<ReportDefinitionDto> GetAsync(Guid id);

    Task<ReportDefinitionDto> GetByCodeAsync(string code);

    Task<ReportDefinitionDto> GetPublishedByCodeAsync(string code);

    Task<ReportDefinitionDto> CreateAsync(CreateReportDefinitionDto input);

    Task<ReportDefinitionDto> CreateFromResourceAsync(CreateReportFromResourceDto input);

    Task<ReportDefinitionDto> UpdateAsync(Guid id, UpdateReportDefinitionDto input);

    Task DeleteAsync(Guid id);

    Task<ReportDefinitionDto> PublishAsync(Guid id);

    Task<ResourceInvokeResultDto> QueryAsync(string code, ResourceRuntimeQueryInput input);
}
