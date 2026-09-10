using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Meta.Dow.IdentityService.Samples;

public class SampleAppService : IdentityServiceAppService, ISampleAppService
{
    public Task<SampleDto> GetAsync()
    {
        return Task.FromResult(new SampleDto { Value = 42 });
    }

    [Authorize]
    public Task<SampleDto> GetAuthorizedAsync()
    {
        return Task.FromResult(new SampleDto { Value = 42 });
    }
}
