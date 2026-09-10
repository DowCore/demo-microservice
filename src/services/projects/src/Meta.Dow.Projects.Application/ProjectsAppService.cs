using Meta.Dow.Projects.Localization;
using Volo.Abp.Application.Services;

namespace Meta.Dow.Projects;

public abstract class ProjectsAppService : ApplicationService
{
    protected ProjectsAppService()
    {
        LocalizationResource = typeof(ProjectsResource);
        ObjectMapperContext = typeof(ProjectsApplicationModule);
    }
}
