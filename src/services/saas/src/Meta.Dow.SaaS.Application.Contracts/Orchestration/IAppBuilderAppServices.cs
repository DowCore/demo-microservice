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

public interface IDbQueryObjectAppService : IApplicationService
{
    Task<PagedResultDto<DbQueryObjectDto>> GetListAsync(DbQueryObjectGetListInput input);

    Task<DbQueryObjectDto> GetAsync(Guid id);

    Task<PagedResultDto<QueryObjectCatalogItemDto>> GetCatalogAsync(QueryObjectCatalogInput input);

    Task<ImportQueryObjectsResultDto> ImportAsync(ImportQueryObjectsInput input);

    Task<DbQueryObjectDto> RefreshAsync(Guid id);

    Task DeleteAsync(Guid id);

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

    Task<ReportDefinitionDto> CreateFromTableAsync(CreateReportFromTableDto input);

    Task<ReportDefinitionDto> UpdateAsync(Guid id, UpdateReportDefinitionDto input);

    Task DeleteAsync(Guid id);

    Task<ReportDefinitionDto> PublishAsync(Guid id);

    Task<ResourceInvokeResultDto> QueryAsync(string code, ResourceRuntimeQueryInput input);
}

public interface IFormDefinitionAppService : IApplicationService
{
    Task<PagedResultDto<FormDefinitionDto>> GetListAsync(FormDefinitionGetListInput input);

    Task<FormDefinitionDto> GetAsync(Guid id);

    Task<FormDefinitionDto> GetByCodeAsync(string code);

    Task<FormDefinitionDto> GetPublishedByCodeAsync(string code);

    Task<FormDefinitionDto> CreateAsync(CreateFormDefinitionDto input);

    Task<FormDefinitionDto> UpdateAsync(Guid id, UpdateFormDefinitionDto input);

    Task DeleteAsync(Guid id);

    Task<FormDefinitionDto> PublishAsync(Guid id);

    /// <summary>已发布表单下拉（报表/OA 绑定 formRef）</summary>
    Task<ListResultDto<FormDefinitionLookupDto>> GetPublishedLookupAsync(string? filter = null);
}

public interface IWorkflowDefinitionAppService : IApplicationService
{
    Task<PagedResultDto<WorkflowDefinitionDto>> GetListAsync(WorkflowDefinitionGetListInput input);

    Task<WorkflowDefinitionDto> GetAsync(Guid id);

    Task<WorkflowDefinitionDto> GetByCodeAsync(string code);

    Task<WorkflowDefinitionDto> CreateAsync(CreateWorkflowDefinitionDto input);

    Task<WorkflowDefinitionDto> UpdateAsync(Guid id, UpdateWorkflowDefinitionDto input);

    Task DeleteAsync(Guid id);

    Task<WorkflowDefinitionDto> PublishAsync(Guid id);
}

public interface IWorkflowRuntimeAppService : IApplicationService
{
    Task<WorkflowInstanceDto> StartAsync(StartWorkflowDto input);

    Task<ListResultDto<WorkflowTaskDto>> GetMyTasksAsync();

    Task<WorkflowInstanceDto> CompleteAsync(Guid taskId, CompleteWorkflowTaskDto input);
}
