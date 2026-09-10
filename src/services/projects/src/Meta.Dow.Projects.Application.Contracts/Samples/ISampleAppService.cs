using System.Threading.Tasks;
using Volo.Abp.Application.Services;

namespace Meta.Dow.Projects.Samples;

public interface ISampleAppService : IApplicationService
{
    Task<SampleDto> GetAsync();

    Task<SampleDto> GetAuthorizedAsync();
}
