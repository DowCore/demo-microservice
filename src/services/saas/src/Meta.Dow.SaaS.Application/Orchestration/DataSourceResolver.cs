using System;
using System.Threading;
using System.Threading.Tasks;
using Volo.Abp;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public interface IDataSourceResolver
{
    Task<DataSource> ResolveEnabledAsync(string code, CancellationToken cancellationToken = default);
}

public class DataSourceResolver : IDataSourceResolver, ITransientDependency
{
    private readonly IRepository<DataSource, Guid> _repository;

    public DataSourceResolver(IRepository<DataSource, Guid> repository)
    {
        _repository = repository;
    }

    public async Task<DataSource> ResolveEnabledAsync(string code, CancellationToken cancellationToken = default)
    {
        var key = Check.NotNullOrWhiteSpace(code, nameof(code)).Trim();
        var entity = await _repository.FirstOrDefaultAsync(x => x.Code == key, cancellationToken);
        if (entity == null)
        {
            throw new UserFriendlyException($"Data source was not found: {key}");
        }

        if (!entity.IsEnabled)
        {
            throw new UserFriendlyException($"Data source is disabled: {key}");
        }

        return entity;
    }
}
