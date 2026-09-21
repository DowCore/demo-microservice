using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class TableDefinitionAppService : SaaSAppService, ITableDefinitionAppService
{
    private readonly IRepository<TableDefinition, Guid> _repository;
    private readonly IRepository<DataSource, Guid> _dataSourceRepository;
    private readonly IRepository<AppResource, Guid> _resourceRepository;
    private readonly ISqlDdlExecutor _ddlExecutor;

    public TableDefinitionAppService(
        IRepository<TableDefinition, Guid> repository,
        IRepository<DataSource, Guid> dataSourceRepository,
        IRepository<AppResource, Guid> resourceRepository,
        ISqlDdlExecutor ddlExecutor
    )
    {
        _repository = repository;
        _dataSourceRepository = dataSourceRepository;
        _resourceRepository = resourceRepository;
        _ddlExecutor = ddlExecutor;
    }

    [Authorize(OrchestrationPermissions.Tables.Default)]
    public async Task<PagedResultDto<TableDefinitionDto>> GetListAsync(TableDefinitionGetListInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var query = await _repository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.DataSourceCode))
        {
            var ds = input.DataSourceCode.Trim();
            query = query.Where(x => x.DataSourceCode == ds);
        }

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            query = query.Where(x => x.TableName.Contains(filter) || x.DisplayName.Contains(filter));
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();
        return new PagedResultDto<TableDefinitionDto>(total, items.Select(Map).ToList());
    }

    [Authorize(OrchestrationPermissions.Tables.Default)]
    public async Task<TableDefinitionDto> GetAsync(Guid id)
    {
        return Map(await _repository.GetAsync(id));
    }

    [Authorize(OrchestrationPermissions.Tables.Create)]
    public async Task<TableDefinitionDto> CreateAsync(CreateTableDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var dsCode = input.DataSourceCode.Trim();
        var ds = await _dataSourceRepository.FirstOrDefaultAsync(x => x.Code == dsCode)
                 ?? throw new UserFriendlyException(L["Orchestration:DataSourceNotFound", dsCode]);
        if (!DataSourceProvider.IsSql(ds.Provider))
        {
            throw new UserFriendlyException(L["Orchestration:TableRequiresSql"]);
        }

        var tableName = input.TableName.Trim();
        if (await _repository.AnyAsync(x => x.DataSourceCode == dsCode && x.TableName == tableName))
        {
            throw new UserFriendlyException(L["Orchestration:TableAlreadyExists", tableName]);
        }

        var entity = new TableDefinition(
            GuidGenerator.Create(),
            dsCode,
            tableName,
            input.DisplayName.Trim(),
            CurrentTenant.Id
        );
        await _repository.InsertAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Tables.Update)]
    public async Task<TableDefinitionDto> UpdateAsync(Guid id, UpdateTableDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await _repository.GetAsync(id);
        entity.UpdateDraft(
            input.DisplayName.Trim(),
            input.Comment,
            input.Columns.Select(MapColumn),
            input.Indexes.Select(MapIndex)
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Tables.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (await _resourceRepository.AnyAsync(x =>
                x.DataSourceCode == entity.DataSourceCode && x.TableName == entity.TableName))
        {
            throw new UserFriendlyException(L["Orchestration:TableInUse"]);
        }

        await _repository.DeleteAsync(id);
    }

    [Authorize(OrchestrationPermissions.Tables.Default)]
    public async Task<ListResultDto<DdlPreviewItemDto>> PreviewAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        var ds = await _dataSourceRepository.FirstOrDefaultAsync(x => x.Code == entity.DataSourceCode)
                 ?? throw new UserFriendlyException(L["Orchestration:DataSourceNotFound", entity.DataSourceCode]);
        var physical = await _ddlExecutor.GetPhysicalColumnsAsync(ds, entity.TableName);
        var existingIndexes = await _ddlExecutor.GetExistingIndexesAsync(ds, entity.TableName);
        var items = _ddlExecutor.Preview(ds, entity, physical, existingIndexes);
        return new ListResultDto<DdlPreviewItemDto>(
            items.Select(x => new DdlPreviewItemDto
            {
                Kind = x.Kind,
                Sql = x.Sql,
                Destructive = x.Destructive
            }).ToList()
        );
    }

    [Authorize(OrchestrationPermissions.Tables.Ddl)]
    public async Task<TableDefinitionDto> ApplyAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        var ds = await _dataSourceRepository.FirstOrDefaultAsync(x => x.Code == entity.DataSourceCode)
                 ?? throw new UserFriendlyException(L["Orchestration:DataSourceNotFound", entity.DataSourceCode]);
        var physical = await _ddlExecutor.GetPhysicalColumnsAsync(ds, entity.TableName);
        var existingIndexes = await _ddlExecutor.GetExistingIndexesAsync(ds, entity.TableName);
        var items = _ddlExecutor.Preview(ds, entity, physical, existingIndexes);
        await _ddlExecutor.ApplyAsync(ds, items);
        entity.MarkApplied();
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Resources.Create)]
    public async Task<AppResourceDto> CreateResourceAsync(Guid id, CreateAppResourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var table = await _repository.GetAsync(id);
        if (table.SyncState != TableSyncState.InSync)
        {
            throw new UserFriendlyException(L["Orchestration:TableNotApplied"]);
        }

        var code = input.Code.Trim();
        if (await _resourceRepository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:ResourceCodeAlreadyExists", code]);
        }

        var entity = new AppResource(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            table.DataSourceCode,
            table.TableName,
            CurrentTenant.Id
        );
        entity.ApplyDefaultsFromTable(table);
        await _resourceRepository.InsertAsync(entity, autoSave: true);
        return AppResourceAppService.Map(entity);
    }

    private static TableColumn MapColumn(TableColumnDto dto)
    {
        return new TableColumn
        {
            Name = dto.Name,
            DisplayName = dto.DisplayName,
            PlatformType = dto.PlatformType,
            Length = dto.Length,
            Precision = dto.Precision,
            Scale = dto.Scale,
            Nullable = dto.Nullable,
            Default = dto.Default,
            Unique = dto.Unique,
            Comment = dto.Comment,
            Origin = dto.Origin,
            AppliedName = dto.AppliedName
        };
    }

    private static TableIndexDef MapIndex(TableIndexDto dto)
    {
        return new TableIndexDef
        {
            Name = dto.Name,
            Unique = dto.Unique,
            IsPrimary = dto.IsPrimary,
            Origin = dto.Origin,
            Columns = dto.Columns.Select(c => new TableIndexColumn
            {
                Name = c.Name,
                Descending = c.Descending
            }).ToList()
        };
    }

    internal static TableDefinitionDto Map(TableDefinition entity)
    {
        return new TableDefinitionDto
        {
            Id = entity.Id,
            DataSourceCode = entity.DataSourceCode,
            TableName = entity.TableName,
            DisplayName = entity.DisplayName,
            Origin = entity.Origin,
            SyncState = entity.SyncState,
            LastAppliedAt = entity.LastAppliedAt,
            Comment = entity.Comment,
            CreationTime = entity.CreationTime,
            Columns = entity.Columns.Select(c => new TableColumnDto
            {
                Name = c.Name,
                DisplayName = c.DisplayName,
                PlatformType = c.PlatformType,
                Length = c.Length,
                Precision = c.Precision,
                Scale = c.Scale,
                Nullable = c.Nullable,
                Default = c.Default,
                Unique = c.Unique,
                Comment = c.Comment,
                Origin = c.Origin,
                AppliedName = c.AppliedName
            }).ToList(),
            Indexes = entity.Indexes.Select(i => new TableIndexDto
            {
                Name = i.Name,
                Unique = i.Unique,
                IsPrimary = i.IsPrimary,
                Origin = i.Origin,
                Columns = i.Columns.Select(c => new TableIndexColumnDto
                {
                    Name = c.Name,
                    Descending = c.Descending
                }).ToList()
            }).ToList()
        };
    }
}
