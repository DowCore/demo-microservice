using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Meta.Dow.SaaS.Orchestration;

public interface ILogicApiAppService : IApplicationService
{
    Task<LogicRunResultDto> RunAsync(string flowKey, string? bodyJson);

    Task<SystemParameterCatalogDto> GetSystemParametersAsync();

    Task<DateExpressionPreviewDto> PreviewDateExpressionAsync(DateExpressionPreviewInput input);
}
