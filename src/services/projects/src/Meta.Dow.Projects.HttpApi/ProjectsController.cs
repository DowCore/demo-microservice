using Meta.Dow.Projects.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Meta.Dow.Projects;

public abstract class ProjectsController : AbpControllerBase
{
    protected ProjectsController()
    {
        LocalizationResource = typeof(ProjectsResource);
    }
}
