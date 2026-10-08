using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class DbQueryObjectAppService : SaaSAppService, IDbQueryObjectAppService
{
    private readonly IRepository<DbQueryObject, Guid> _repository;
    private readonly IRepository<DataSource, Guid> _dataSourceRepository;
    private readonly IRepository<AppResource, Guid> _resourceRepository;
    private readonly ISqlCatalogInspector _catalogInspector;

    public DbQueryObjectAppService(
        IRepository<DbQueryObject, Guid> repository,
        IRepository<DataSource, Guid> dataSourceRepository,
        IRepository<AppResource, Guid> resourceRepository,
        ISqlCatalogInspector catalogInspector
    )
    {
        _repository = repository;
        _dataSourceRepository = dataSourceRepository;
        _resourceRepository = resourceRepository;
        _catalogInspector = catalogInspector;
    }

    [Authorize(OrchestrationPermissions.QueryObjects.Default)]
    public async Task<PagedResultDto<DbQueryObjectDto>> GetListAsync(DbQueryObjectGetListInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var query = await _repository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.DataSourceCode))
        {
            var ds = input.DataSourceCode.Trim();
            query = query.Where(x => x.DataSourceCode == ds);
        }

        if (!string.IsNullOrWhiteSpace(input.Kind))
        {
            var kind = QueryObjectKind.Normalize(input.Kind);
            query = query.Where(x => x.Kind == kind);
        }

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            query = query.Where(x => x.ObjectName.Contains(filter) || x.DisplayName.Contains(filter));
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();
        var bound = await ResolveBoundResourceIdsAsync(items.Select(x => x.Id).ToList());
        return new PagedResultDto<DbQueryObjectDto>(
            total,
            items.Select(x => Map(x, bound.GetValueOrDefault(x.Id))).ToList()
        );
    }

    [Authorize(OrchestrationPermissions.QueryObjects.Default)]
    public async Task<DbQueryObjectDto> GetAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        var bound = await ResolveBoundResourceIdsAsync([entity.Id]);
        return Map(entity, bound.GetValueOrDefault(entity.Id));
    }

    [Authorize(OrchestrationPermissions.QueryObjects.Default)]
    public async Task<PagedResultDto<QueryObjectCatalogItemDto>> GetCatalogAsync(QueryObjectCatalogInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ds = await ResolveSqlDataSourceAsync(input.DataSourceCode);
        var kind = QueryObjectKind.Normalize(input.Kind);
        var max = input.MaxResultCount <= 0 ? 20 : Math.Min(input.MaxResultCount, 200);
        var nameFilter = input.ResolveNameFilter();
        var (catalog, total) = await _catalogInspector.ListPageAsync(
            ds,
            kind,
            nameFilter,
            input.SkipCount,
            max
        );
        var imported = (await _repository.GetQueryableAsync())
            .Where(x => x.DataSourceCode == ds.Code && x.Kind == kind)
            .Select(x => x.ObjectName)
            .ToList()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new PagedResultDto<QueryObjectCatalogItemDto>(
            total,
            catalog.Select(x => new QueryObjectCatalogItemDto
            {
                ObjectName = x.ObjectName,
                DisplayName = x.DisplayName,
                Kind = x.Kind,
                RoutineKind = x.RoutineKind,
                CanSelectFrom = x.CanSelectFrom,
                Imported = imported.Contains(x.ObjectName)
            }).ToList()
        );
    }

    [Authorize(OrchestrationPermissions.QueryObjects.Create)]
    public async Task<ImportQueryObjectsResultDto> ImportAsync(ImportQueryObjectsInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var ds = await ResolveSqlDataSourceAsync(input.DataSourceCode);
        var kind = QueryObjectKind.Normalize(input.Kind);
        var result = new ImportQueryObjectsResultDto();
        var names = (input.Names ?? [])
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (names.Count == 0)
        {
            throw new UserFriendlyException(L["Orchestration:QueryObjectNamesRequired"]);
        }

        foreach (var name in names)
        {
            try
            {
                var details = await _catalogInspector.DescribeAsync(ds, kind, name);
                var entity = await _repository.FirstOrDefaultAsync(x =>
                    x.DataSourceCode == ds.Code && x.Kind == kind && x.ObjectName == details.ObjectName);
                if (entity == null)
                {
                    entity = new DbQueryObject(
                        GuidGenerator.Create(),
                        ds.Code,
                        kind,
                        details.ObjectName,
                        details.DisplayName,
                        CurrentTenant.Id
                    );
                    entity.ApplyImport(
                        details.DisplayName,
                        details.RoutineKind,
                        details.CanSelectFrom,
                        details.Comment,
                        details.ImportWarning,
                        details.Columns,
                        details.Parameters
                    );
                    await _repository.InsertAsync(entity, autoSave: true);
                }
                else
                {
                    entity.ApplyImport(
                        entity.DisplayName,
                        details.RoutineKind,
                        details.CanSelectFrom,
                        details.Comment,
                        details.ImportWarning,
                        details.Columns,
                        details.Parameters
                    );
                    await _repository.UpdateAsync(entity, autoSave: true);
                    result.Skipped.Add(name + "（已刷新）");
                }

                result.Items.Add(Map(entity));
            }
            catch (Exception ex)
            {
                result.Errors.Add(name + "：" + ex.Message);
            }
        }

        return result;
    }

    [Authorize(OrchestrationPermissions.QueryObjects.Update)]
    public async Task<DbQueryObjectDto> RefreshAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        var ds = await ResolveSqlDataSourceAsync(entity.DataSourceCode);
        var details = await _catalogInspector.DescribeAsync(ds, entity.Kind, entity.ObjectName);
        entity.ApplyImport(
            entity.DisplayName,
            details.RoutineKind,
            details.CanSelectFrom,
            details.Comment,
            details.ImportWarning,
            details.Columns,
            details.Parameters
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        var bound = await ResolveBoundResourceIdsAsync([entity.Id]);
        return Map(entity, bound.GetValueOrDefault(entity.Id));
    }

    [Authorize(OrchestrationPermissions.QueryObjects.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (await _resourceRepository.AnyAsync(x =>
                x.QueryObjectId == entity.Id ||
                (x.DataSourceCode == entity.DataSourceCode &&
                 x.TableName == entity.ObjectName &&
                 x.SourceKind == entity.Kind)))
        {
            throw new UserFriendlyException(L["Orchestration:QueryObjectInUse"]);
        }

        await _repository.DeleteAsync(id);
    }

    [Authorize(OrchestrationPermissions.Resources.Create)]
    public async Task<AppResourceDto> CreateResourceAsync(Guid id, CreateAppResourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var queryObject = await _repository.GetAsync(id);

        // 已进入过设计：直接返回已绑定资源
        var existingBound = await _resourceRepository.FirstOrDefaultAsync(x => x.QueryObjectId == queryObject.Id);
        if (existingBound != null)
        {
            return AppResourceAppService.Map(existingBound);
        }

        var code = input.Code.Trim();
        var existingByCode = await _resourceRepository.FirstOrDefaultAsync(x => x.Code == code);
        if (existingByCode != null)
        {
            // 同数据源+对象的查询资源：视为已设计，补绑后打开
            if (existingByCode.DataSourceCode == queryObject.DataSourceCode &&
                existingByCode.TableName == queryObject.ObjectName &&
                existingByCode.IsQueryCatalog())
            {
                if (existingByCode.QueryObjectId != queryObject.Id)
                {
                    existingByCode.BindQueryObject(queryObject);
                    await _resourceRepository.UpdateAsync(existingByCode, autoSave: true);
                }

                return AppResourceAppService.Map(existingByCode);
            }

            throw new UserFriendlyException(L["Orchestration:ResourceCodeAlreadyExists", code]);
        }

        var entity = new AppResource(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            queryObject.DataSourceCode,
            queryObject.ObjectName,
            CurrentTenant.Id
        );
        entity.ApplyDefaultsFromQueryObject(queryObject);
        await _resourceRepository.InsertAsync(entity, autoSave: true);
        return AppResourceAppService.Map(entity);
    }

    private async Task<DataSource> ResolveSqlDataSourceAsync(string dataSourceCode)
    {
        var code = Check.NotNullOrWhiteSpace(dataSourceCode, nameof(dataSourceCode)).Trim();
        var ds = await _dataSourceRepository.FirstOrDefaultAsync(x => x.Code == code)
                 ?? throw new UserFriendlyException(L["Orchestration:DataSourceNotFound", code]);
        if (!DataSourceProvider.IsSql(ds.Provider))
        {
            throw new UserFriendlyException(L["Orchestration:QueryObjectRequiresSql"]);
        }

        return ds;
    }

    private async Task<Dictionary<Guid, Guid?>> ResolveBoundResourceIdsAsync(List<Guid> queryObjectIds)
    {
        var result = new Dictionary<Guid, Guid?>();
        if (queryObjectIds.Count == 0)
        {
            return result;
        }

        var resourceQuery = await _resourceRepository.GetQueryableAsync();
        var pairs = resourceQuery
            .Where(x => x.QueryObjectId != null && queryObjectIds.Contains(x.QueryObjectId.Value))
            .Select(x => new { QueryObjectId = x.QueryObjectId!.Value, x.Id })
            .ToList();
        foreach (var g in pairs.GroupBy(x => x.QueryObjectId))
        {
            result[g.Key] = g.First().Id;
        }

        return result;
    }

    internal static DbQueryObjectDto Map(DbQueryObject entity, Guid? boundResourceId = null)
    {
        return new DbQueryObjectDto
        {
            Id = entity.Id,
            DataSourceCode = entity.DataSourceCode,
            Kind = entity.Kind,
            ObjectName = entity.ObjectName,
            DisplayName = entity.DisplayName,
            RoutineKind = entity.RoutineKind,
            CanSelectFrom = entity.CanSelectFrom,
            Comment = entity.Comment,
            ImportWarning = entity.ImportWarning,
            LastImportedAt = entity.LastImportedAt,
            BoundResourceId = boundResourceId,
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
            Parameters = entity.Parameters.Select(p => new QueryObjectParameterDto
            {
                Name = p.Name,
                DisplayName = p.DisplayName,
                PlatformType = p.PlatformType,
                Direction = p.Direction,
                Ordinal = p.Ordinal,
                Nullable = p.Nullable,
                HasDefault = p.HasDefault
            }).ToList()
        };
    }
}
