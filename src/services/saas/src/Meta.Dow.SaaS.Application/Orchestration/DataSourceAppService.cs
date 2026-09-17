using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class DataSourceAppService : SaaSAppService, IDataSourceAppService
{
    private readonly IRepository<DataSource, Guid> _repository;

    public DataSourceAppService(IRepository<DataSource, Guid> repository)
    {
        _repository = repository;
    }

    [Authorize(OrchestrationPermissions.DataSources.Default)]
    public async Task<PagedResultDto<DataSourceDto>> GetListAsync(DataSourceGetListInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var query = await _repository.GetQueryableAsync();

        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            query = query.Where(x => x.Name.Contains(filter) || x.Code.Contains(filter));
        }

        if (input.EnabledOnly == true)
        {
            query = query.Where(x => x.IsEnabled);
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();

        return new PagedResultDto<DataSourceDto>(total, items.Select(MapToDto).ToList());
    }

    [Authorize(OrchestrationPermissions.DataSources.Default)]
    public async Task<DataSourceDto> GetAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        return MapToDto(entity);
    }

    /// <summary>设计器下拉：启用中的数据源（定义编辑者可用）</summary>
    [Authorize(OrchestrationPermissions.Definitions.Default)]
    public async Task<ListResultDto<DataSourceLookupDto>> GetLookupAsync()
    {
        var query = await _repository.GetQueryableAsync();
        var items = query
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToList()
            .Select(x => new DataSourceLookupDto
            {
                Code = x.Code,
                Name = x.Name,
                Provider = x.Provider,
                Family = DataSourceProvider.GetFamily(x.Provider),
                AccessMode = x.AccessMode
            })
            .ToList();

        return new ListResultDto<DataSourceLookupDto>(items);
    }

    [Authorize(OrchestrationPermissions.DataSources.Default)]
    public Task<ListResultDto<DataSourceProviderOptionDto>> GetProvidersAsync()
    {
        var items = DataSourceProvider.All
            .Select(p => new DataSourceProviderOptionDto
            {
                Provider = p,
                DisplayName = DataSourceProvider.DisplayName(p),
                Family = DataSourceProvider.GetFamily(p),
                ConnectionHint = DataSourceProvider.ConnectionHint(p)
            })
            .ToList();
        return Task.FromResult(new ListResultDto<DataSourceProviderOptionDto>(items));
    }

    [Authorize(OrchestrationPermissions.DataSources.Create)]
    public async Task<DataSourceDto> CreateAsync(CreateDataSourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var code = input.Code.Trim();
        if (await _repository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:DataSourceCodeAlreadyExists", code]);
        }

        var entity = new DataSource(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            input.Provider,
            input.ConnectionString,
            input.AccessMode,
            input.AllowedOps,
            input.Description,
            CurrentTenant.Id
        );

        await _repository.InsertAsync(entity, autoSave: true);
        return MapToDto(entity);
    }

    [Authorize(OrchestrationPermissions.DataSources.Update)]
    public async Task<DataSourceDto> UpdateAsync(Guid id, UpdateDataSourceDto input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var entity = await _repository.GetAsync(id);
        entity.Update(
            input.Name.Trim(),
            input.Provider,
            input.AccessMode,
            input.AllowedOps,
            input.Description,
            input.IsEnabled,
            input.ConnectionString
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        return MapToDto(entity);
    }

    [Authorize(OrchestrationPermissions.DataSources.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        await _repository.DeleteAsync(id);
    }

    private static DataSourceDto MapToDto(DataSource entity)
    {
        return new DataSourceDto
        {
            Id = entity.Id,
            Code = entity.Code,
            Name = entity.Name,
            Provider = entity.Provider,
            Family = DataSourceProvider.GetFamily(entity.Provider),
            AccessMode = entity.AccessMode,
            AllowedOps = entity.AllowedOps.ToList(),
            IsEnabled = entity.IsEnabled,
            Description = entity.Description,
            HasConnectionString = !string.IsNullOrWhiteSpace(entity.ConnectionString),
            ConnectionStringHint = MaskConnectionString(entity.ConnectionString),
            CreationTime = entity.CreationTime,
            LastModificationTime = entity.LastModificationTime
        };
    }

    internal static string? MaskConnectionString(string? cs)
    {
        if (string.IsNullOrWhiteSpace(cs))
        {
            return null;
        }

        var parts = cs.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var masked = parts.Select(p =>
        {
            var idx = p.IndexOf('=');
            if (idx <= 0)
            {
                return p;
            }

            var key = p[..idx].Trim();
            var value = p[(idx + 1)..];
            if (key.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("pwd", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("secret", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("user", StringComparison.OrdinalIgnoreCase))
            {
                return $"{key}=***";
            }

            if (value.Length > 24)
            {
                return $"{key}={value[..8]}…";
            }

            return p;
        });

        return string.Join(';', masked);
    }
}
