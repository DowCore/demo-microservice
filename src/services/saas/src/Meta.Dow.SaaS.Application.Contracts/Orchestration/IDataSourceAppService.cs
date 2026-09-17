using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Meta.Dow.SaaS.Orchestration;

public interface IDataSourceAppService : IApplicationService
{
    Task<PagedResultDto<DataSourceDto>> GetListAsync(DataSourceGetListInput input);

    Task<DataSourceDto> GetAsync(Guid id);

    Task<ListResultDto<DataSourceLookupDto>> GetLookupAsync();

    Task<ListResultDto<DataSourceProviderOptionDto>> GetProvidersAsync();

    Task<DataSourceDto> CreateAsync(CreateDataSourceDto input);

    Task<DataSourceDto> UpdateAsync(Guid id, UpdateDataSourceDto input);

    Task DeleteAsync(Guid id);
}
