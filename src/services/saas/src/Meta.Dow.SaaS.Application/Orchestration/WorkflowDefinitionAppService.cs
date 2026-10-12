using System;
using System.Linq;
using System.Threading.Tasks;
using Meta.Dow.SaaS.Permissions;
using Microsoft.AspNetCore.Authorization;
using Volo.Abp;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Domain.Repositories;

namespace Meta.Dow.SaaS.Orchestration;

public class WorkflowDefinitionAppService : SaaSAppService, IWorkflowDefinitionAppService
{
    private readonly IRepository<WorkflowDefinition, Guid> _repository;
    private readonly IRepository<FormDefinition, Guid> _formRepository;
    private readonly IRepository<WorkflowInstance, Guid> _instanceRepository;

    public WorkflowDefinitionAppService(
        IRepository<WorkflowDefinition, Guid> repository,
        IRepository<FormDefinition, Guid> formRepository,
        IRepository<WorkflowInstance, Guid> instanceRepository
    )
    {
        _repository = repository;
        _formRepository = formRepository;
        _instanceRepository = instanceRepository;
    }

    [Authorize(OrchestrationPermissions.Workflows.Default)]
    public async Task<PagedResultDto<WorkflowDefinitionDto>> GetListAsync(
        WorkflowDefinitionGetListInput input
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        var query = await _repository.GetQueryableAsync();
        if (!string.IsNullOrWhiteSpace(input.Filter))
        {
            var filter = input.Filter.Trim();
            query = query.Where(x => x.Name.Contains(filter) || x.Code.Contains(filter));
        }

        if (input.Status.HasValue)
        {
            query = query.Where(x => x.Status == input.Status.Value);
        }

        var total = query.Count();
        var items = query
            .OrderByDescending(x => x.CreationTime)
            .Skip(input.SkipCount)
            .Take(input.MaxResultCount)
            .ToList();
        return new PagedResultDto<WorkflowDefinitionDto>(total, items.Select(Map).ToList());
    }

    [Authorize(OrchestrationPermissions.Workflows.Default)]
    public async Task<WorkflowDefinitionDto> GetAsync(Guid id) =>
        Map(await _repository.GetAsync(id));

    [Authorize(OrchestrationPermissions.Workflows.Default)]
    public async Task<WorkflowDefinitionDto> GetByCodeAsync(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new UserFriendlyException(L["Orchestration:WorkflowKeyRequired"]);
        }

        var key = code.Trim();
        var entity = await _repository.FirstOrDefaultAsync(x => x.Code == key);
        if (entity == null)
        {
            throw new UserFriendlyException(L["Orchestration:WorkflowNotFound", key]);
        }

        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Workflows.Create)]
    public async Task<WorkflowDefinitionDto> CreateAsync(CreateWorkflowDefinitionDto input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var code = input.Code.Trim();
        if (await _repository.AnyAsync(x => x.Code == code))
        {
            throw new UserFriendlyException(L["Orchestration:WorkflowCodeAlreadyExists", code]);
        }

        await EnsureFormRefAsync(input.FormRef);

        var entity = new WorkflowDefinition(
            GuidGenerator.Create(),
            code,
            input.Name.Trim(),
            CurrentTenant.Id
        );
        entity.UpdateDraft(
            input.Name.Trim(),
            input.Description,
            input.FormRef,
            WorkflowDefinition.DefaultProcessJson(),
            null,
            null
        );
        await _repository.InsertAsync(entity, autoSave: true);
        return Map(entity);
    }

    [Authorize(OrchestrationPermissions.Workflows.Update)]
    public async Task<WorkflowDefinitionDto> UpdateAsync(
        Guid id,
        UpdateWorkflowDefinitionDto input
    )
    {
        ArgumentNullException.ThrowIfNull(input);
        var entity = await _repository.GetAsync(id);
        await EnsureFormRefAsync(input.FormRef);
        entity.UpdateDraft(
            input.Name.Trim(),
            input.Description,
            input.FormRef,
            input.ProcessJson ?? WorkflowDefinition.DefaultProcessJson(),
            input.BeforeStartFlowKey,
            input.AfterEndFlowKey
        );
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    /// <summary>删除（F6）：有在途实例（running）时拒绝删除，避免在途单据失去定义。</summary>
    [Authorize(OrchestrationPermissions.Workflows.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (await _instanceRepository.AnyAsync(x => x.WorkflowCode == entity.Code && x.Status == "running"))
        {
            throw new UserFriendlyException($"审批流「{entity.Code}」仍有在途实例，无法删除");
        }

        await _repository.DeleteAsync(id, autoSave: true);
    }

    [Authorize(OrchestrationPermissions.Workflows.Publish)]
    public async Task<WorkflowDefinitionDto> PublishAsync(Guid id)
    {
        var entity = await _repository.GetAsync(id);
        if (string.IsNullOrWhiteSpace(entity.FormRef))
        {
            throw new UserFriendlyException(L["Orchestration:WorkflowFormRefRequired"]);
        }

        var form = await EnsureFormRefAsync(entity.FormRef, publishedOnly: true);

        // 发布前做结构校验：把配置错误拦在发布口，而不是运行时爆发。
        WorkflowDefinitionValidator.Validate(entity.ProcessJson, CollectFormFields(form));

        entity.Publish();
        await _repository.UpdateAsync(entity, autoSave: true);
        return Map(entity);
    }

    private async Task<FormDefinition?> EnsureFormRefAsync(string? formRef, bool publishedOnly = false)
    {
        if (string.IsNullOrWhiteSpace(formRef))
        {
            return null;
        }

        var key = formRef.Trim();
        var form = await _formRepository.FirstOrDefaultAsync(x => x.Code == key);
        if (form == null)
        {
            throw new UserFriendlyException(L["Orchestration:FormNotFound", key]);
        }

        if (publishedOnly && form.Status != AppResourceStatus.Published)
        {
            throw new UserFriendlyException(L["Orchestration:FormNotPublished", key]);
        }

        return form;
    }

    /// <summary>收集绑定表单的字段名（含明细子字段），用于条件字段引用校验。</summary>
    private static List<string> CollectFormFields(FormDefinition? form)
    {
        var result = new List<string>();
        if (form?.Schema?.Fields is not { Count: > 0 } fields)
        {
            return result;
        }

        void Walk(IEnumerable<FormFieldDef> items)
        {
            foreach (var field in items)
            {
                if (!string.IsNullOrWhiteSpace(field.Field))
                {
                    result.Add(field.Field);
                }

                if (field.Children is { Count: > 0 } children)
                {
                    Walk(children);
                }
            }
        }

        Walk(fields);
        return result;
    }

    private static WorkflowDefinitionDto Map(WorkflowDefinition e) =>
        new()
        {
            Id = e.Id,
            Code = e.Code,
            Name = e.Name,
            Description = e.Description,
            Status = e.Status,
            Version = e.Version,
            FormRef = e.FormRef,
            ProcessJson = e.ProcessJson,
            BeforeStartFlowKey = e.BeforeStartFlowKey,
            AfterEndFlowKey = e.AfterEndFlowKey,
            CreationTime = e.CreationTime
        };
}
